import type { SeedDirectoryConfig, SeedLocalDirectoryConfig } from './directory.js';

/** Auth-Teil der Laufzeitkonfiguration (config.json), kommt aus dem Terraform-Modul sso. */
export interface SeedAuthConfig {
  /** Client-ID der SPA-App-Registrierung. */
  clientId: string;
  tenantId: string;
  /** Delegierte Berechtigung der API, z. B. api://<client-id>/access_as_user. */
  apiScope: string;
  /**
   * Pfad der Bridge-Seite für die stille Anmeldung im iframe, z. B. /redirect.html. Das Modul sso
   * trägt die passende Redirect-URI ein; ohne Bridge-Seite erneuert seed-web-auth per Umleitung.
   */
  redirectBridgePath?: string;
  /** Personen und Fotos aus Microsoft Graph (seed-web-people); fehlt, wenn das Feature aus ist. */
  directory?: SeedDirectoryConfig;
}

/** Auth-Teil von config.json für die lokale Entwicklung: kein MSAL, die API kennt den Nutzer. */
export interface SeedLocalAuthConfig {
  mode: 'local';
  /** Erfundene Personen für seed-web-people; Graph gibt es lokal nicht. */
  directory?: SeedLocalDirectoryConfig;
}

/** Prüft, ob die Laufzeitkonfiguration einen vollständigen Auth-Teil enthält. */
export function isSeedAuthConfig(value: unknown): value is SeedAuthConfig {
  const config = value as Partial<SeedAuthConfig> | null | undefined;
  return (
    typeof config?.clientId === 'string' && config.clientId !== '' &&
    typeof config.tenantId === 'string' && config.tenantId !== '' &&
    typeof config.apiScope === 'string' && config.apiScope !== ''
  );
}

/** Prüft, ob config.json den lokalen Modus verlangt (auth.mode: "local"). */
export function isSeedLocalAuthConfig(value: unknown): value is SeedLocalAuthConfig {
  return (value as Partial<SeedLocalAuthConfig> | null | undefined)?.mode === 'local';
}
