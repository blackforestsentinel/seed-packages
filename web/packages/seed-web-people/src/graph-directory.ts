import type { SeedDirectoryConfig } from '@blackforestsentinel/seed-web-auth';
import { defaultStorage, TtlCache } from './cache.js';
import { DirectoryError, type Directory, type SearchOptions } from './directory.js';
import { createGraphClient, GraphError, type GraphResponse } from './graph-client.js';
import type { Person, PhotoSize, Presence, PresenceStatus } from './types.js';

/** Lebensdauer der Zwischenspeicher in Millisekunden. */
export interface DirectoryTtl {
  /** Profile, auch „gibt es nicht“; Default 30 Minuten. */
  person: number;
  /** Fotos, auch „kein Foto“; Default 30 Minuten. */
  photo: number;
  /** Suchergebnisse, nur im Speicher; Default 2 Minuten. */
  search: number;
  /** Teams-Status, nur im Speicher; Default 1 Minute. */
  presence: number;
  /** Personen mit App-Rolle; Default 10 Minuten. */
  roles: number;
}

export interface GraphDirectoryOptions {
  /** auth.directory aus config.json. */
  config: SeedDirectoryConfig;
  /** Token für Graph, z. B. (scopes) => auth.getAccessToken(scopes) aus seed-web-auth. */
  getAccessToken: (scopes: string[]) => Promise<string>;
  fetch?: typeof fetch;
  baseUrl?: string;
  /** Trennt den Zwischenspeicher je Konto, z. B. homeAccountId. */
  cacheKey?: string;
  /** Default sessionStorage; null hält alles nur im Speicher. */
  storage?: Storage | null;
  ttl?: Partial<DirectoryTtl>;
  /** Sammelzeit für $batch in Millisekunden, Default 10. */
  batchDelay?: number;
  /** Abstand, in dem sichtbare Status neu geladen werden, Default 60 Sekunden. */
  presenceInterval?: number;
  now?: () => number;
  sleep?: (ms: number) => Promise<void>;
}

const DEFAULT_TTL: DirectoryTtl = {
  person: 30 * 60_000,
  photo: 30 * 60_000,
  search: 2 * 60_000,
  presence: 60_000,
  roles: 10 * 60_000,
};

const USER_SELECT = 'id,displayName,mail,userPrincipalName,jobTitle,department,officeLocation';
const BASIC_SELECT = 'id,displayName,mail,userPrincipalName';
const PEOPLE_SELECT = 'id,displayName,jobTitle,department,officeLocation,userPrincipalName,scoredEmailAddresses,personType';
const NO_ROLE = '00000000-0000-0000-0000-000000000000';
const PRESENCE_LIMIT = 650;
const TOKEN_PAUSE = 5 * 60_000;

interface GraphUser {
  id: string;
  displayName?: string | null;
  mail?: string | null;
  userPrincipalName?: string | null;
  jobTitle?: string | null;
  department?: string | null;
  officeLocation?: string | null;
}

interface GraphPerson extends GraphUser {
  scoredEmailAddresses?: { address?: string | null }[];
  personType?: { class?: string; subclass?: string };
}

interface GraphPresence {
  id: string;
  availability?: string;
  activity?: string;
  outOfOfficeSettings?: { isOutOfOffice?: boolean } | null;
}

/**
 * Verzeichnis aus Microsoft Graph mit delegierten Berechtigungen: Profile, Fotos, Suche, Teams-Status
 * und Personen mit App-Rolle. Alles läuft im Browser mit dem Token der angemeldeten Person; Fotos und
 * Profile gehen nie an die eigene API.
 */
export function createGraphDirectory(options: GraphDirectoryOptions): Directory {
  const { config } = options;
  const now = options.now ?? Date.now;
  const ttl = { ...DEFAULT_TTL, ...options.ttl };
  const storage = options.storage === undefined ? defaultStorage() : options.storage;
  const cache = new TtlCache(`seed-people.${options.cacheKey ?? 'default'}.`, storage, now);

  // Ohne Token (fehlende Einwilligung, Netz) pausiert das Paket Graph eine Weile, statt jede
  // Komponente einzeln scheitern zu lassen.
  let pausedUntil = 0;
  let pauseReason: unknown = null;
  let warned = false;
  async function getToken() {
    if (now() < pausedUntil) {
      throw pauseReason;
    }
    try {
      return await options.getAccessToken(config.scopes);
    } catch (error) {
      if (!(error instanceof Error && error.name === 'SeedAuthRedirectError')) {
        pausedUntil = now() + TOKEN_PAUSE;
        pauseReason = error;
        if (!warned) {
          warned = true;
          console.warn(
            'seed-web-people: Kein Token für Microsoft Graph, Fotos und Personensuche bleiben aus. Fehlt die Einwilligung für die Graph-Berechtigungen?',
            error,
          );
        }
      }
      throw error;
    }
  }

  const client = createGraphClient({
    getToken,
    fetch: options.fetch,
    baseUrl: options.baseUrl,
    batchDelay: options.batchDelay,
    sleep: options.sleep,
  });

  async function getUser(path: string, headers?: Record<string, string>) {
    const separator = path.includes('?') ? '&' : '?';
    let response = await client.get(`${path}${separator}$select=${USER_SELECT}`, { headers });
    // Reicht die Berechtigung nicht für Position und Abteilung, nur die Grunddaten.
    if (response.status === 403) {
      response = await client.get(`${path}${separator}$select=${BASIC_SELECT}`, { headers });
    }
    return response;
  }

  // --- Teams-Status ---------------------------------------------------------------------------
  let presenceAvailable = config.presence === true;
  const presence = new Map<string, { value: Presence; fetched: number }>();
  const listeners = new Map<string, Set<() => void>>();
  const presenceQueue = new Set<string>();
  let presenceTimer: ReturnType<typeof setTimeout> | null = null;
  let presenceInterval: ReturnType<typeof setInterval> | null = null;

  function notify(id: string) {
    listeners.get(id)?.forEach((listener) => listener());
  }

  function queuePresence(id: string) {
    presenceQueue.add(id);
    presenceTimer ??= setTimeout(() => void flushPresence(), 50);
  }

  async function flushPresence() {
    presenceTimer = null;
    const ids = [...presenceQueue];
    presenceQueue.clear();
    for (let i = 0; i < ids.length && presenceAvailable; i += PRESENCE_LIMIT) {
      let response: GraphResponse;
      try {
        response = await client.send('POST', '/communications/getPresencesByUserId', { ids: ids.slice(i, i + PRESENCE_LIMIT) });
      } catch {
        continue; // nächster Versuch im nächsten Intervall
      }
      if (response.status === 403) {
        presenceAvailable = false;
        console.warn('seed-web-people: Kein Zugriff auf den Teams-Status (Presence.Read.All). Der Status bleibt aus.');
        [...listeners.keys()].forEach(notify);
        return;
      }
      if (response.status >= 400) {
        continue;
      }
      for (const entry of (response.body as { value?: GraphPresence[] } | null)?.value ?? []) {
        const next = toPresence(entry);
        const current = presence.get(entry.id);
        // Gleicher Status behält dasselbe Objekt, damit nichts neu rendert.
        const value = current && samePresence(current.value, next) ? current.value : next;
        presence.set(entry.id, { value, fetched: now() });
        if (value !== current?.value) {
          notify(entry.id);
        }
      }
    }
  }

  function refreshPresence() {
    if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
      return;
    }
    for (const id of listeners.keys()) {
      queuePresence(id);
    }
  }

  // --- App-Rollen -----------------------------------------------------------------------------
  const roleLoads = new Map<string, Promise<Person[]>>();

  async function getAll<T>(path: string, permission: string, headers?: Record<string, string>): Promise<T[]> {
    const items: T[] = [];
    let next: string | null = path;
    while (next) {
      const response = await client.get(next, { headers });
      if (response.status >= 400) {
        throw graphFailure(response, permission);
      }
      const body = response.body as { value?: T[]; '@odata.nextLink'?: string } | null;
      items.push(...(body?.value ?? []));
      next = body?.['@odata.nextLink'] ? relativePath(body['@odata.nextLink']) : null;
    }
    return items;
  }

  async function loadRoleMembers(apiClientId: string, wanted: string[] | null): Promise<Person[]> {
    const servicePrincipal = `/servicePrincipals(appId='${encodeURIComponent(apiClientId)}')`;
    const app = await client.get(`${servicePrincipal}?$select=id,appRoles`);
    if (app.status >= 400) {
      throw graphFailure(app, 'Application.Read.All', `API-Registrierung ${apiClientId} nicht gefunden.`);
    }
    const appRoles = (app.body as { appRoles?: { id: string; value?: string | null }[] } | null)?.appRoles ?? [];
    let roleIds: Set<string> | null = null;
    if (wanted) {
      const unknown = wanted.filter((value) => !appRoles.some((role) => role.value === value));
      if (unknown.length > 0) {
        console.warn(`seed-web-people: Unbekannte App-Rollen ${unknown.join(', ')}; die API kennt ${appRoles.map((role) => role.value).join(', ')}.`);
      }
      roleIds = new Set(appRoles.filter((role) => role.value && wanted.includes(role.value)).map((role) => role.id));
    }

    const assignments = await getAll<{ principalId: string; principalType: string; principalDisplayName?: string | null; appRoleId: string }>(
      `${servicePrincipal}/appRoleAssignedTo?$select=principalId,principalType,principalDisplayName,appRoleId&$top=999`,
      'Application.Read.All',
    );
    const relevant = assignments.filter((assignment) => assignment.appRoleId !== NO_ROLE && (!roleIds || roleIds.has(assignment.appRoleId)));

    const members = new Map<string, Person>();
    for (const assignment of relevant) {
      if (assignment.principalType === 'User') {
        members.set(assignment.principalId, { id: assignment.principalId, displayName: assignment.principalDisplayName ?? '' });
      }
    }
    const groups = [...new Set(relevant.filter((assignment) => assignment.principalType === 'Group').map((assignment) => assignment.principalId))];
    const groupMembers = await Promise.all(
      groups.map((group) =>
        getAll<GraphUser>(
          `/groups/${encodeURIComponent(group)}/transitiveMembers/microsoft.graph.user?$select=${BASIC_SELECT}&$top=999&$count=true`,
          'GroupMember.Read.All',
          { ConsistencyLevel: 'eventual' },
        ),
      ),
    );
    for (const user of groupMembers.flat()) {
      members.set(user.id, { ...members.get(user.id), ...toPerson(user) });
    }
    return [...members.values()].sort((a, b) => a.displayName.localeCompare(b.displayName, 'de'));
  }

  return {
    mode: 'graph',
    get presence() {
      return presenceAvailable;
    },
    roleFilter: config.roleFilter === true && !!config.apiClientId,

    peekPerson: (id) => cache.get<Person | null>(`person:${id}`),

    async getPerson(id) {
      const key = `person:${id}`;
      const cached = cache.get<Person | null>(key);
      if (cached !== undefined) {
        return cached;
      }
      const response = await getUser(`/users/${encodeURIComponent(id)}`);
      if (response.status === 404 || response.status === 400) {
        cache.set(key, null, ttl.person);
        return null;
      }
      if (response.status >= 400) {
        throw graphFailure(response, 'User.ReadBasic.All');
      }
      const person = toPerson(response.body as GraphUser);
      cache.set(key, person, ttl.person);
      return person;
    },

    peekPhoto: (id, size) => cache.get<string | null>(`photo:${size}:${id}`),

    async getPhoto(id: string, size: PhotoSize) {
      const key = `photo:${size}:${id}`;
      const cached = cache.get<string | null>(key);
      if (cached !== undefined) {
        return cached;
      }
      const response = await client.get(`/users/${encodeURIComponent(id)}/photos/${size}x${size}/$value`, { binary: true });
      if (response.status === 200 && typeof response.body === 'string') {
        const type = (response.headers['content-type'] ?? 'image/jpeg').split(';')[0];
        const url = `data:${type};base64,${response.body}`;
        cache.set(key, url, ttl.photo);
        return url;
      }
      // Kein Foto (404) merken; andere Fehler nur kurz und nur im Speicher.
      if (response.status === 404 || response.status === 400) {
        cache.set(key, null, ttl.photo);
      } else {
        cache.set(key, null, 60_000, false);
      }
      return null;
    },

    async search(text: string, searchOptions: SearchOptions = {}) {
      const query = text.replace(/["\\]/g, ' ').replace(/\s+/g, ' ').trim();
      if (!query) {
        return [];
      }
      const top = searchOptions.top ?? 10;
      const key = `search:${top}:${query.toLowerCase()}`;
      const cached = cache.get<Person[]>(key);
      if (cached) {
        return cached;
      }

      // Beide gehen gemeinsam in einem $batch: erst die für die Person relevanten Kontakte, dann
      // das Verzeichnis. Ohne People.Read bleibt es bei der Verzeichnissuche.
      const [relevant, directory] = await Promise.allSettled([
        client.get(`/me/people?$search=${encodeURIComponent(`"${query}"`)}&$top=${top}&$select=${PEOPLE_SELECT}`),
        getUser(`/users?$search=${encodeURIComponent(`"displayName:${query}" OR "mail:${query}"`)}&$top=${top}`, {
          ConsistencyLevel: 'eventual',
        }),
      ]);

      const results = new Map<string, Person>();
      let succeeded = false;
      if (relevant.status === 'fulfilled' && relevant.value.status < 400) {
        succeeded = true;
        for (const entry of (relevant.value.body as { value?: GraphPerson[] } | null)?.value ?? []) {
          if (entry.id && (!entry.personType || entry.personType.subclass === 'OrganizationUser')) {
            results.set(entry.id, { ...toPerson(entry), mail: entry.scoredEmailAddresses?.[0]?.address ?? entry.userPrincipalName ?? null });
          }
        }
      }
      if (directory.status === 'fulfilled' && directory.value.status < 400) {
        succeeded = true;
        for (const entry of (directory.value.body as { value?: GraphUser[] } | null)?.value ?? []) {
          const person = toPerson(entry);
          cache.set(`person:${person.id}`, person, ttl.person);
          if (!results.has(person.id)) {
            results.set(person.id, person);
          }
        }
      }
      if (!succeeded) {
        throw directory.status === 'rejected'
          ? directory.reason
          : graphFailure(directory.value, 'User.ReadBasic.All');
      }
      const people = [...results.values()];
      cache.set(key, people, ttl.search, false);
      return people;
    },

    peekPresence: (id) => presence.get(id)?.value,

    subscribePresence(id, listener) {
      if (!presenceAvailable) {
        return () => {};
      }
      let set = listeners.get(id);
      if (!set) {
        set = new Set();
        listeners.set(id, set);
      }
      set.add(listener);
      const known = presence.get(id);
      if (!known || known.fetched + ttl.presence <= now()) {
        queuePresence(id);
      }
      presenceInterval ??= setInterval(refreshPresence, options.presenceInterval ?? ttl.presence);
      return () => {
        set.delete(listener);
        if (set.size === 0) {
          listeners.delete(id);
        }
        if (listeners.size === 0 && presenceInterval !== null) {
          clearInterval(presenceInterval);
          presenceInterval = null;
        }
      };
    },

    async getAppRoleMembers(roles) {
      if (!config.roleFilter || !config.apiClientId) {
        throw new DirectoryError(
          'not-configured',
          'Der Filter nach App-Rolle ist nicht eingerichtet: auth.directory.roleFilter und apiClientId fehlen in config.json.',
        );
      }
      const wanted = roles && roles.length > 0 ? [...new Set(roles)].sort() : null;
      const key = `roles:${wanted?.join(',') ?? '*'}`;
      const cached = cache.get<Person[]>(key);
      if (cached) {
        return cached;
      }
      let pending = roleLoads.get(key);
      if (!pending) {
        pending = loadRoleMembers(config.apiClientId, wanted)
          .then((members) => {
            cache.set(key, members, ttl.roles);
            return members;
          })
          .finally(() => roleLoads.delete(key));
        roleLoads.set(key, pending);
      }
      return pending;
    },

    clear() {
      cache.clear();
      presence.clear();
    },
  };
}

function toPerson(user: GraphUser): Person {
  return {
    id: user.id,
    displayName: user.displayName || user.userPrincipalName || user.mail || '',
    mail: user.mail ?? null,
    userPrincipalName: user.userPrincipalName ?? null,
    jobTitle: user.jobTitle ?? null,
    department: user.department ?? null,
    officeLocation: user.officeLocation ?? null,
  };
}

const AVAILABILITY: Record<string, PresenceStatus> = {
  Available: 'available',
  AvailableIdle: 'available',
  Away: 'away',
  BeRightBack: 'away',
  Busy: 'busy',
  BusyIdle: 'busy',
  DoNotDisturb: 'do-not-disturb',
  Offline: 'offline',
};

/** Übersetzt den Graph-Status in die Status von Fluent. */
export function toPresence(entry: Pick<GraphPresence, 'availability' | 'activity' | 'outOfOfficeSettings'>): Presence {
  const availability = entry.availability ?? 'PresenceUnknown';
  const activity = entry.activity ?? '';
  let status = AVAILABILITY[availability] ?? 'unknown';
  if (activity === 'OutOfOffice' || (entry.outOfOfficeSettings?.isOutOfOffice && (status === 'offline' || status === 'away' || status === 'unknown'))) {
    status = 'out-of-office';
  }
  return { status, availability, activity };
}

function samePresence(a: Presence, b: Presence) {
  return a.status === b.status && a.availability === b.availability && a.activity === b.activity;
}

function relativePath(link: string): string {
  const url = new URL(link);
  return url.pathname.replace(/^\/(v1\.0|beta)(?=\/)/, '') + url.search;
}

function graphFailure(response: GraphResponse, permission: string, notFound?: string): Error {
  const message = (response.body as { error?: { message?: string } } | null)?.error?.message;
  if (response.status === 403) {
    return new DirectoryError('forbidden', `Keine Berechtigung in Microsoft Graph (${permission}, Admin-Consent erteilt?)${message ? `: ${message}` : ''}`);
  }
  if (response.status === 404 && notFound) {
    return new DirectoryError('not-found', notFound);
  }
  return new GraphError(response.status, `Graph-Anfrage fehlgeschlagen (HTTP ${response.status})${message ? `: ${message}` : ''}`);
}
