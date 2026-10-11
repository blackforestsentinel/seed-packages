/** Person aus dem Verzeichnis (Microsoft Graph oder lokale Liste). */
export interface Person {
  /** Objekt-ID in Entra ID, lokal die ID aus config.json. */
  id: string;
  displayName: string;
  mail?: string | null;
  userPrincipalName?: string | null;
  jobTitle?: string | null;
  department?: string | null;
  officeLocation?: string | null;
}

/** Teams-Status, angelehnt an die Status von Fluent. */
export type PresenceStatus = 'available' | 'away' | 'busy' | 'do-not-disturb' | 'offline' | 'out-of-office' | 'unknown';

export interface Presence {
  status: PresenceStatus;
  /** Rohwerte aus Graph, z. B. Available und InAMeeting. */
  availability: string;
  activity: string;
}

/** Feste Fotogrößen in Graph, die das Paket lädt. */
export type PhotoSize = 48 | 64 | 96;

/** graph: Daten aus Microsoft Graph; local: Liste aus config.json; off: kein Verzeichnis. */
export type DirectoryMode = 'graph' | 'local' | 'off';

/** Ladezustand der Hooks; idle heißt: nichts zu laden (keine ID, Feature aus). */
export type LoadStatus = 'idle' | 'loading' | 'ready' | 'error';
