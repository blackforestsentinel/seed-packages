import { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { roleDisplayNames, useSeedUser } from '@blackforestsentinel/seed-web-auth/react';
import { DirectoryError, photoSizeFor, principalTypesOf, type Directory } from './directory.js';
import { useDirectory, useDirectoryAuth } from './provider.js';
import type { LoadStatus, Person, Presence, PrincipalKind, ServicePrincipalType } from './types.js';

// Stabile leere Werte, damit abhängige useMemo nicht bei jedem Rendern neu rechnen.
const NO_PEOPLE: Person[] = [];

interface ResourceState<T> {
  key: string | null;
  status: LoadStatus;
  value: T | undefined;
  error?: unknown;
}

function initialState<T>(key: string | null, peek: () => T | undefined): ResourceState<T> {
  if (key === null) {
    return { key, status: 'idle', value: undefined };
  }
  const cached = peek();
  return cached === undefined ? { key, status: 'loading', value: undefined } : { key, status: 'ready', value: cached };
}

/**
 * Lädt einen Wert je Schlüssel; Werte aus dem Zwischenspeicher sind sofort da, ohne Ladezustand.
 * Wechselt der Schlüssel, gilt der alte Wert nicht mehr.
 */
function useResource<T>(key: string | null, directory: Directory, peek: () => T | undefined, load: () => Promise<T>): ResourceState<T> {
  const [state, setState] = useState<ResourceState<T>>(() => initialState(key, peek));

  useEffect(() => {
    if (key === null) {
      return;
    }
    const cached = peek();
    if (cached !== undefined) {
      setState((current) => (current.key === key && current.value === cached ? current : { key, status: 'ready', value: cached }));
      return;
    }
    let active = true;
    setState((current) => (current.key === key && current.status === 'loading' ? current : { key, status: 'loading', value: undefined }));
    load().then(
      (value) => active && setState({ key, status: 'ready', value }),
      (error: unknown) => active && setState({ key, status: 'error', value: undefined, error }),
    );
    return () => {
      active = false;
    };
    // peek und load hängen nur an key und directory.
  }, [key, directory]);

  return state.key === key ? state : initialState(key, peek);
}

export interface PersonState {
  status: LoadStatus;
  /** Profil oder null (unbekannt, nicht gefunden, Verzeichnis aus). */
  person: Person | null;
  error?: unknown;
}

/**
 * Profil einer Person aus dem Verzeichnis (Graph /users/{id} oder lokale Liste); mit
 * auth.directory.servicePrincipals auch einer Anwendung oder verwalteten Identität.
 */
export function usePerson(id: string | null | undefined): PersonState {
  const directory = useDirectory();
  const state = useResource(
    id ? `person:${id}` : null,
    directory,
    () => directory.peekPerson(id!),
    () => directory.getPerson(id!),
  );
  return { status: state.status, person: state.value ?? null, error: state.error };
}

/**
 * Art des Eintrags: bekannt, aus dem Profil oder offen (undefined), solange das Profil noch lädt.
 * resolving heißt: Das Verzeichnis kennt Service Principals, die Art steht erst mit dem Profil fest.
 */
export function resolvePrincipalKind(known: PrincipalKind | undefined, loaded: PersonState, resolving: boolean): PrincipalKind | undefined {
  return known ?? loaded.person?.kind ?? (resolving && loaded.status === 'loading' ? undefined : 'user');
}

export interface PhotoState {
  status: LoadStatus;
  /** Foto als data:-URL oder null (kein Foto, Verzeichnis aus). */
  url: string | null;
  error?: unknown;
}

/** Profilbild in passender Größe für die Anzeigegröße in Pixeln (48, 64 oder 96 aus Graph). */
export function usePhoto(id: string | null | undefined, size = 48): PhotoState {
  const directory = useDirectory();
  const photoSize = photoSizeFor(size);
  const state = useResource(
    id ? `photo:${photoSize}:${id}` : null,
    directory,
    () => directory.peekPhoto(id!, photoSize),
    () => directory.getPhoto(id!, photoSize),
  );
  return { status: state.status, url: state.value ?? null, error: state.error };
}

export interface PeopleSearchOptions {
  /** Ab so vielen Zeichen wird gesucht, Default 2. */
  minChars?: number;
  /** Wartezeit nach dem letzten Tastendruck, Default 250 ms. */
  debounceMs?: number;
  /** Höchstzahl je Quelle, Default 10. */
  top?: number;
  /** false schaltet die Suche aus. */
  enabled?: boolean;
  /** Default nur user; mit servicePrincipal auch Anwendungen und verwaltete Identitäten. */
  principalTypes?: readonly PrincipalKind[];
  /** Nur diese Arten von Service Principals, etwa ['ManagedIdentity']. */
  servicePrincipalTypes?: readonly ServicePrincipalType[];
}

export interface PeopleSearchState {
  /** idle: zu kurz oder ausgeschaltet; loading auch während der Wartezeit. */
  status: LoadStatus;
  people: Person[];
  /** Text, zu dem people gehören. */
  query: string;
  error?: unknown;
}

/**
 * Personensuche: zuerst die für die angemeldete Person relevanten (Graph /me/people), dann das
 * Verzeichnis (/users?$search), auf Wunsch auch Service Principals (/servicePrincipals?$search).
 * Lokal sucht sie in auth.directory.people.
 */
export function usePeopleSearch(text: string, options: PeopleSearchOptions = {}): PeopleSearchState {
  const { minChars = 2, debounceMs = 250, top = 10, enabled = true, principalTypes, servicePrincipalTypes } = options;
  const typeKey = [...new Set(principalTypesOf({ principalTypes }))].sort().join(',');
  const principalTypeKey = servicePrincipalTypes?.length ? [...new Set(servicePrincipalTypes)].sort().join(',') : '*';
  const directory = useDirectory();
  const trimmed = text.trim();
  const active = enabled && directory.mode !== 'off' && trimmed.length >= minChars;
  const [debounced, setDebounced] = useState(active ? trimmed : '');

  useEffect(() => {
    if (!active) {
      setDebounced('');
      return;
    }
    const timer = setTimeout(() => setDebounced(trimmed), debounceMs);
    return () => clearTimeout(timer);
  }, [trimmed, active, debounceMs]);

  const state = useResource(
    active && debounced ? `search:${top}:${typeKey}:${principalTypeKey}:${debounced}` : null,
    directory,
    () => undefined,
    () => directory.search(debounced, { top, principalTypes, servicePrincipalTypes }),
  );

  if (!active) {
    return { status: 'idle', people: NO_PEOPLE, query: '' };
  }
  if (debounced !== trimmed) {
    return { status: 'loading', people: NO_PEOPLE, query: trimmed };
  }
  return { status: state.status, people: state.value ?? NO_PEOPLE, query: debounced, error: state.error };
}

export interface PresenceState {
  /** idle: kein Status verfügbar (Feature aus, keine ID). */
  status: LoadStatus;
  presence: Presence | null;
}

/** Teams-Status, gebündelt für alle sichtbaren Personen und regelmäßig aktualisiert. */
export function usePresence(id: string | null | undefined): PresenceState {
  const directory = useDirectory();
  const enabled = !!id && directory.presence;
  const subscribe = useCallback(
    (listener: () => void) => (id && directory.presence ? directory.subscribePresence(id, listener) : () => {}),
    [directory, id],
  );
  const presence = useSyncExternalStore(
    subscribe,
    () => (enabled ? directory.peekPresence(id!) : undefined),
    () => undefined,
  );
  return { status: enabled ? (presence ? 'ready' : 'loading') : 'idle', presence: presence ?? null };
}

export interface AppRoleMembersState {
  status: LoadStatus;
  /** Personen mit einer der Rollen, Gruppen aufgelöst. */
  members: Person[];
  ids: ReadonlySet<string>;
  error?: unknown;
}

const NO_IDS: ReadonlySet<string> = new Set();

/**
 * Personen mit App-Rolle der API (Graph appRoleAssignedTo, Gruppen per transitiveMembers), mit
 * principalTypes auch Service Principals mit direkter Zuweisung. Ohne auth.directory.roleFilter:
 * status error mit DirectoryError('not-configured').
 */
export function useAppRoleMembers(
  roles?: readonly string[],
  options: { enabled?: boolean; principalTypes?: readonly PrincipalKind[] } = {},
): AppRoleMembersState {
  const { enabled = true, principalTypes } = options;
  const directory = useDirectory();
  const roleKey = roles && roles.length > 0 ? [...new Set(roles)].sort().join(',') : '*';
  const typeKey = [...new Set(principalTypesOf({ principalTypes }))].sort().join(',');
  const configured = directory.roleFilter;
  const state = useResource(
    enabled && configured ? `roles:${typeKey}:${roleKey}` : null,
    directory,
    () => undefined,
    () => directory.getAppRoleMembers(roles, { principalTypes }),
  );
  const members = state.value;
  const ids = useMemo(() => (members ? new Set(members.map((person) => person.id)) : NO_IDS), [members]);

  if (enabled && !configured) {
    return {
      status: 'error',
      members: NO_PEOPLE,
      ids: NO_IDS,
      error:
        directory.mode === 'off'
          ? new DirectoryError('not-configured', 'Das Verzeichnis ist nicht eingerichtet (auth.directory fehlt in config.json).')
          : new DirectoryError('not-configured', 'Der Filter nach App-Rolle ist nicht eingerichtet: auth.directory.roleFilter fehlt in config.json.'),
    };
  }
  return { status: state.status, members: members ?? NO_PEOPLE, ids, error: state.error };
}

/** Angemeldete Person für die Anzeige, zusammengesetzt aus Anmeldung (MSAL) und /api/me. */
export interface CurrentUser {
  /** Objekt-ID in Entra ID; lokal die ID, die die API nennt. */
  id: string | null;
  displayName: string | null;
  /** Anmeldename, meist die Mail-Adresse. */
  username: string | null;
  roles: string[];
  /** Anzeigenamen der Rollen aus roleDetails, sonst die Werte. */
  roleNames: string[];
  capabilities: string[];
}

export interface CurrentUserState {
  /** loading, solange /api/me lädt; der Name aus der Anmeldung ist dann schon da. */
  status: 'loading' | 'ready' | 'error';
  /** null: niemand angemeldet. */
  user: CurrentUser | null;
  error?: unknown;
}

/** Angemeldete Person: Name sofort aus der Anmeldung, Rollen und Capabilities aus /api/me. */
export function useCurrentUser(): CurrentUserState {
  const seed = useSeedUser();
  const account = useDirectoryAuth()?.account ?? null;
  const accountId = account ? ((account.idTokenClaims?.oid as string | undefined) ?? account.localAccountId ?? null) : null;
  const accountName = account?.name ?? null;
  const accountUsername = account?.username ?? null;

  return useMemo(() => {
    const user = seed.user;
    const error = seed.status === 'error' ? seed.error : undefined;
    if (!user && !account) {
      return { status: seed.status, user: null, error };
    }
    return {
      status: seed.status,
      user: {
        id: user?.objectId ?? accountId,
        displayName: user?.name ?? accountName ?? user?.username ?? accountUsername,
        username: user?.username ?? accountUsername,
        roles: user?.roles ?? [],
        roleNames: roleDisplayNames(user),
        capabilities: user?.capabilities ?? [],
      },
      error,
    };
    // account selbst ist bei jedem Lesen ein neues Objekt; es zählen seine Werte.
  }, [seed, !!account, accountId, accountName, accountUsername]);
}
