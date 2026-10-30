import type { SeedLocalPerson } from '@blackforestsentinel/seed-web-auth';
import type { DirectoryMode, Person, PhotoSize, Presence, PrincipalKind, ServicePrincipalType } from './types.js';

/** Fehler des Verzeichnisses mit einem Code, nach dem die Oberfläche unterscheiden kann. */
export class DirectoryError extends Error {
  readonly code: 'not-configured' | 'forbidden' | 'not-found' | 'unavailable';

  constructor(code: DirectoryError['code'], message: string, options?: { cause?: unknown }) {
    super(message, options);
    this.name = 'DirectoryError';
    this.code = code;
  }
}

export interface SearchOptions {
  /** Höchstzahl je Quelle, Default 10. */
  top?: number;
  /** Wonach gesucht wird, Default nur user. servicePrincipal braucht auth.directory.servicePrincipals. */
  principalTypes?: readonly PrincipalKind[];
  /** Nur diese Arten von Service Principals, etwa ['ManagedIdentity']; Default alle. */
  servicePrincipalTypes?: readonly ServicePrincipalType[];
}

export interface AppRoleMembersOptions {
  /**
   * Default nur user. Mit servicePrincipal auch Anwendungen und verwaltete Identitäten, denen die
   * Rolle direkt zugewiesen ist; braucht auth.directory.servicePrincipals.
   */
  principalTypes?: readonly PrincipalKind[];
}

const USER_ONLY: readonly PrincipalKind[] = ['user'];

/** Gewünschte Arten, Default nur user; ohne Eintrag ebenfalls user. */
export function principalTypesOf(options: { principalTypes?: readonly PrincipalKind[] } | undefined): readonly PrincipalKind[] {
  const types = options?.principalTypes;
  return types && types.length > 0 ? types : USER_ONLY;
}

/**
 * Datenquelle der Komponenten. createGraphDirectory liest aus Microsoft Graph,
 * createLocalDirectory aus einer festen Liste. Wer eigene Daten hat, kann die Schnittstelle selbst
 * umsetzen und an DirectoryProvider übergeben.
 */
export interface Directory {
  readonly mode: DirectoryMode;
  /** Teams-Status verfügbar. */
  readonly presence: boolean;
  /** Filter nach App-Rolle verfügbar (getAppRoleMembers). */
  readonly roleFilter: boolean;
  /** Service Principals verfügbar (Suche, Profile, App-Rollen); fehlt es, gilt false. */
  readonly servicePrincipals?: boolean;
  /** Profil aus dem Cache: Person, null (gibt es nicht) oder undefined (noch unbekannt). */
  peekPerson(id: string): Person | null | undefined;
  getPerson(id: string): Promise<Person | null>;
  /** Foto aus dem Cache als data:-URL, null (kein Foto) oder undefined (noch unbekannt). */
  peekPhoto(id: string, size: PhotoSize): string | null | undefined;
  getPhoto(id: string, size: PhotoSize): Promise<string | null>;
  /**
   * Personen zum Suchtext, relevante zuerst; mit principalTypes auch Service Principals. Wirft
   * DirectoryError('not-configured'), wenn Service Principals gewünscht, aber nicht eingerichtet sind.
   */
  search(text: string, options?: SearchOptions): Promise<Person[]>;
  /** Letzter bekannter Status oder undefined. */
  peekPresence(id: string): Presence | undefined;
  /** Meldet Änderungen am Status, solange jemand ihn zeigt; liefert die Abmeldung. */
  subscribePresence(id: string, listener: () => void): () => void;
  /**
   * Personen mit einer App-Rolle der API, Gruppen aufgelöst. Ohne roles alle mit irgendeiner Rolle.
   * Wirft DirectoryError('not-configured'), wenn der Filter nicht eingerichtet ist.
   */
  getAppRoleMembers(roles?: readonly string[], options?: AppRoleMembersOptions): Promise<Person[]>;
  /** Leert alle Zwischenspeicher, etwa beim Abmelden. */
  clear(): void;
}

/** Fotogröße in Graph für eine Anzeigegröße in Pixeln, mit Reserve für hochauflösende Bildschirme. */
export function photoSizeFor(size: number): PhotoSize {
  if (size <= 24) {
    return 48;
  }
  return size <= 32 ? 64 : 96;
}

/** Ob die Person zum Suchtext passt: Wortanfang im Namen oder Anfang der Mail-Adresse oder App-ID. */
export function matchesSearch(person: Person, text: string): boolean {
  const query = normalize(text);
  if (!query) {
    return false;
  }
  const name = normalize(person.displayName);
  const words = name.split(/[\s,.\-()]+/);
  return (
    words.some((word) => word.startsWith(query)) ||
    name.startsWith(query) ||
    normalize(person.mail ?? '').startsWith(query) ||
    normalize(person.userPrincipalName ?? '').startsWith(query) ||
    normalize(person.appId ?? '').startsWith(query)
  );
}

function normalize(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().trim();
}

/**
 * Verzeichnis aus einer festen Liste, für den lokalen Modus (auth.directory.people in config.json)
 * und für Tests. Keine Fotos, kein Status. Der Filter nach App-Rolle nutzt roles der Personen;
 * Einträge mit kind servicePrincipal stehen für Anwendungen.
 */
export function createLocalDirectory(people: readonly SeedLocalPerson[] = []): Directory {
  const byId = new Map(people.map((person) => [person.id, person]));
  const toPerson = (person: SeedLocalPerson): Person =>
    person.kind === 'servicePrincipal'
      ? {
          id: person.id,
          displayName: person.displayName,
          kind: 'servicePrincipal',
          appId: person.appId ?? null,
          servicePrincipalType: person.servicePrincipalType ?? null,
        }
      : {
          id: person.id,
          displayName: person.displayName,
          kind: 'user',
          mail: person.mail ?? null,
          jobTitle: person.jobTitle ?? null,
          department: person.department ?? null,
        };
  const ofTypes = (options: SearchOptions | undefined) => {
    const types = principalTypesOf(options);
    const spTypes = options?.servicePrincipalTypes;
    return (person: Person) =>
      person.kind === 'servicePrincipal'
        ? types.includes('servicePrincipal') && (!spTypes || spTypes.includes(person.servicePrincipalType ?? ''))
        : types.includes('user');
  };

  return {
    mode: 'local',
    presence: false,
    roleFilter: true,
    servicePrincipals: true,
    peekPerson: (id) => {
      const person = byId.get(id);
      return person ? toPerson(person) : null;
    },
    getPerson: async (id) => {
      const person = byId.get(id);
      return person ? toPerson(person) : null;
    },
    peekPhoto: () => null,
    getPhoto: async () => null,
    search: async (text, options = {}) =>
      people
        .map(toPerson)
        .filter(ofTypes(options))
        .filter((person) => matchesSearch(person, text))
        .slice(0, options.top ?? 10),
    peekPresence: () => undefined,
    subscribePresence: () => () => {},
    getAppRoleMembers: async (roles, options) =>
      people
        .filter((person) => (person.roles ?? []).some((role) => !roles || roles.length === 0 || roles.includes(role)))
        .map(toPerson)
        .filter(ofTypes(options)),
    clear: () => {},
  };
}

/**
 * Verzeichnis ohne Daten, wenn auth.directory fehlt: Die Komponenten zeigen nur, was die App
 * übergibt (Namen und Initialen), und rufen Graph nie auf.
 */
export function createEmptyDirectory(): Directory {
  return {
    mode: 'off',
    presence: false,
    roleFilter: false,
    servicePrincipals: false,
    peekPerson: () => null,
    getPerson: async () => null,
    peekPhoto: () => null,
    getPhoto: async () => null,
    search: async () => [],
    peekPresence: () => undefined,
    subscribePresence: () => () => {},
    getAppRoleMembers: async () => {
      throw new DirectoryError('not-configured', 'Das Verzeichnis ist nicht eingerichtet (auth.directory fehlt in config.json).');
    },
    clear: () => {},
  };
}
