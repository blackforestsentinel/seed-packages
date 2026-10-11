import type { SeedLocalPerson } from '@blackforestsentinel/seed-web-auth';
import type { DirectoryMode, Person, PhotoSize, Presence } from './types.js';

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
  /** Profil aus dem Cache: Person, null (gibt es nicht) oder undefined (noch unbekannt). */
  peekPerson(id: string): Person | null | undefined;
  getPerson(id: string): Promise<Person | null>;
  /** Foto aus dem Cache als data:-URL, null (kein Foto) oder undefined (noch unbekannt). */
  peekPhoto(id: string, size: PhotoSize): string | null | undefined;
  getPhoto(id: string, size: PhotoSize): Promise<string | null>;
  /** Personen zum Suchtext, relevante zuerst. */
  search(text: string, options?: SearchOptions): Promise<Person[]>;
  /** Letzter bekannter Status oder undefined. */
  peekPresence(id: string): Presence | undefined;
  /** Meldet Änderungen am Status, solange jemand ihn zeigt; liefert die Abmeldung. */
  subscribePresence(id: string, listener: () => void): () => void;
  /**
   * Personen mit einer App-Rolle der API, Gruppen aufgelöst. Ohne roles alle mit irgendeiner Rolle.
   * Wirft DirectoryError('not-configured'), wenn der Filter nicht eingerichtet ist.
   */
  getAppRoleMembers(roles?: readonly string[]): Promise<Person[]>;
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

/** Ob die Person zum Suchtext passt: Wortanfang im Namen oder Anfang der Mail-Adresse. */
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
    normalize(person.userPrincipalName ?? '').startsWith(query)
  );
}

function normalize(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().trim();
}

/**
 * Verzeichnis aus einer festen Liste, für den lokalen Modus (auth.directory.people in config.json)
 * und für Tests. Keine Fotos, kein Status. Der Filter nach App-Rolle nutzt roles der Personen.
 */
export function createLocalDirectory(people: readonly SeedLocalPerson[] = []): Directory {
  const byId = new Map(people.map((person) => [person.id, person]));
  const toPerson = (person: SeedLocalPerson): Person => ({
    id: person.id,
    displayName: person.displayName,
    mail: person.mail ?? null,
    jobTitle: person.jobTitle ?? null,
    department: person.department ?? null,
  });

  return {
    mode: 'local',
    presence: false,
    roleFilter: true,
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
        .filter((person) => matchesSearch(person, text))
        .slice(0, options.top ?? 10),
    peekPresence: () => undefined,
    subscribePresence: () => () => {},
    getAppRoleMembers: async (roles) =>
      people
        .filter((person) => (person.roles ?? []).some((role) => !roles || roles.length === 0 || roles.includes(role)))
        .map(toPerson),
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
