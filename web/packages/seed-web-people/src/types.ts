/** Art des Eintrags: Benutzerkonto oder Service Principal (Anwendung, verwaltete Identität). */
export type PrincipalKind = 'user' | 'servicePrincipal';

/**
 * servicePrincipalType aus Entra ID. Gebräuchlich sind ManagedIdentity und Application; Graph
 * kennt außerdem Legacy und SocialIdp.
 */
export type ServicePrincipalType = 'ManagedIdentity' | 'Application' | 'Legacy' | 'SocialIdp' | (string & {});

/**
 * Person aus dem Verzeichnis (Microsoft Graph oder lokale Liste), mit auth.directory.servicePrincipals
 * auch eine Anwendung oder verwaltete Identität.
 */
export interface Person {
  /** Objekt-ID in Entra ID, lokal die ID aus config.json. */
  id: string;
  displayName: string;
  /** Das Verzeichnis setzt es immer; fehlt es, etwa in gespeicherten Daten, gilt user. */
  kind?: PrincipalKind;
  mail?: string | null;
  userPrincipalName?: string | null;
  jobTitle?: string | null;
  department?: string | null;
  officeLocation?: string | null;
  /** Nur Service Principals: Client-ID der Anwendung. */
  appId?: string | null;
  /** Nur Service Principals. */
  servicePrincipalType?: ServicePrincipalType | null;
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
