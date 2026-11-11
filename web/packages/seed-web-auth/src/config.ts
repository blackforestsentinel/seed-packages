import type { SeedDirectoryConfig, SeedLocalDirectoryConfig } from './directory.js';

/** Auth-Teil der Laufzeitkonfiguration (config.json), kommt aus dem Terraform-Modul sso. */
export interface SeedAuthConfig {
  /** Client-ID der SPA-App-Registrierung. */
  clientId: string;
  /** Tenant-ID (GUID). common, organizations oder ein Domänenname gelten nicht. */
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

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Wirft, wenn tenantId keine GUID ist. Mit common oder organizations nähme die Anmeldung Konten
 * jedes Tenants an; die API lehnte deren Tokens zwar ab, das Frontend liefe aber mit einer fremden
 * Sitzung gegen Graph. Der lokale Modus hat keine tenantId und kommt hier nicht vorbei.
 */
export function assertTenantId(config: Pick<SeedAuthConfig, 'tenantId'>): void {
  if (typeof config.tenantId !== 'string' || !GUID.test(config.tenantId)) {
    throw new Error(`seed-web-auth: tenantId „${String(config.tenantId)}“ ist keine Tenant-ID (GUID). Steht in config.json die Ausgabe des Terraform-Moduls sso?`);
  }
}

/** Prüft, ob config.json den lokalen Modus verlangt (auth.mode: "local"). */
export function isSeedLocalAuthConfig(value: unknown): value is SeedLocalAuthConfig {
  return (value as Partial<SeedLocalAuthConfig> | null | undefined)?.mode === 'local';
}
