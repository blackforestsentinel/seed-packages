/**
 * Personen, Fotos und Teams-Status aus Microsoft Graph für @blackforestsentinel/seed-web-people.
 * Teil auth.directory in config.json; das Terraform-Modul sso schreibt ihn, wenn project.yaml
 * auth.directory einschaltet. Ohne diesen Teil ruft niemand Graph auf.
 */
export interface SeedDirectoryConfig {
  /**
   * Delegierte Graph-Berechtigungen, für die das Frontend ein Token holt, z. B.
   * https://graph.microsoft.com/User.ReadBasic.All. Das Modul sso trägt dieselben an der
   * SPA-Registrierung ein.
   */
  scopes: string[];
  /** Teams-Status laden (Presence.Read.All). */
  presence?: boolean;
  /**
   * Personen nach App-Rolle filtern. Braucht Application.Read.All und GroupMember.Read.All, beide
   * mit Admin-Consent.
   */
  roleFilter?: boolean;
  /**
   * Service Principals (Anwendungen, verwaltete Identitäten) suchen und anzeigen. Braucht
   * Application.Read.All mit Admin-Consent.
   */
  servicePrincipals?: boolean;
  /** Client-ID der API-Registrierung; deren App-Rollen nennt der Filter. */
  apiClientId?: string;
}

/** Erfundene Person oder Anwendung für den lokalen Modus. */
export interface SeedLocalPerson {
  id: string;
  displayName: string;
  /** user (Default) oder servicePrincipal für eine Anwendung oder verwaltete Identität. */
  kind?: 'user' | 'servicePrincipal';
  mail?: string;
  jobTitle?: string;
  department?: string;
  /** Nur Service Principals: Client-ID der Anwendung. */
  appId?: string;
  /** Nur Service Principals: ManagedIdentity oder Application. */
  servicePrincipalType?: string;
  /** App-Rollen (Werte wie in project.yaml) für den Personenfilter nach Rolle. */
  roles?: string[];
}

/** auth.directory im lokalen Modus: kein Graph, der Picker sucht in dieser Liste. */
export interface SeedLocalDirectoryConfig {
  people?: SeedLocalPerson[];
}
