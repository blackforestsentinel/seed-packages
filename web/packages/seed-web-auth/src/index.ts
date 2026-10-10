import {
  AuthError,
  BrowserAuthErrorCodes,
  CacheLookupPolicy,
  createStandardPublicClientApplication,
  InteractionRequiredAuthError,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser';

export { hasCapability, loadSeedUser } from './user.js';
export type { SeedUser, SeedUserSource } from './user.js';

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
}

/** Auth-Teil von config.json für die lokale Entwicklung: kein MSAL, die API kennt den Nutzer. */
export interface SeedLocalAuthConfig {
  mode: 'local';
}

export interface SeedAuthOptions {
  /**
   * Ziel nach Login und Logout. Default ist der Origin der Seite mit abschließendem
   * Schrägstrich, so wie das Terraform-Modul sso ihn an der App-Registrierung hinterlegt.
   */
  redirectUri?: string;
  /** Redirect-URI der Bridge-Seite. Default ist der Origin plus redirectBridgePath aus der Konfiguration. */
  redirectBridgeUri?: string;
  /** Eigene MSAL-Instanz, etwa für Tests. */
  client?: IPublicClientApplication;
}

export interface SeedAuth {
  /** Angemeldetes Konto oder null. */
  readonly account: AccountInfo | null;
  login(): Promise<void>;
  logout(): Promise<void>;
  /** Access-Token für die API; passt zu getAccessToken von createHttpClient (seed-web-core). */
  getAccessToken(): Promise<string>;
}

/** Wird geworfen, wenn für ein Token eine Anmeldung nötig ist und die Seite dafür umleitet. */
export class SeedAuthRedirectError extends Error {
  constructor() {
    super('Anmeldung nötig, die Seite wird umgeleitet.');
    this.name = 'SeedAuthRedirectError';
  }
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

/**
 * Fehler, bei denen der Browser Entra ID gar nicht erreicht hat. Dann ist die Sitzung nicht
 * verloren, und eine Umleitung hilft nicht.
 */
const TRANSIENT_ERRORS = new Set<string>([
  BrowserAuthErrorCodes.noNetworkConnectivity,
  BrowserAuthErrorCodes.postRequestFailed,
  BrowserAuthErrorCodes.getRequestFailed,
  BrowserAuthErrorCodes.failedToParseResponse,
]);

/**
 * Ob nach einem Fehler beim stillen Erneuern nur noch eine Anmeldung hilft. Bewusst eine Liste der
 * vorübergehenden Fehler statt der endgültigen: Ein abgelaufenes Refresh-Token endet je nach Browser
 * in timed_out (MSAL 5 ohne Bridge-Seite: redirect_bridge_timeout), empty_response oder ähnlichem,
 * nicht in einem InteractionRequiredAuthError. Wer nur diesen abfängt, bleibt ohne Token hängen.
 */
function requiresInteraction(error: unknown): boolean {
  if (error instanceof InteractionRequiredAuthError) {
    return true;
  }
  return error instanceof AuthError && !TRANSIENT_ERRORS.has(error.errorCode);
}

/**
 * Richtet MSAL ein und schließt eine laufende Anmeldung per Redirect ab. Danach ist
 * account gesetzt, wenn die Person angemeldet ist.
 */
export async function createSeedAuth(config: SeedAuthConfig, options: SeedAuthOptions = {}): Promise<SeedAuth> {
  const origin = typeof window === 'undefined' ? '' : window.location.origin;
  const redirectUri = options.redirectUri ?? `${origin}/`;
  const bridgeUri = options.redirectBridgeUri ?? (config.redirectBridgePath ? `${origin}${config.redirectBridgePath}` : undefined);
  const client =
    options.client ??
    (await createStandardPublicClientApplication({
      auth: {
        clientId: config.clientId,
        authority: `https://login.microsoftonline.com/${config.tenantId}`,
        redirectUri,
        postLogoutRedirectUri: redirectUri,
      },
      cache: { cacheLocation: 'sessionStorage' },
    }));
  const scopes = [config.apiScope];

  // Der Benutzername als login_hint überdauert den Tab (die Tokens liegen nur im sessionStorage);
  // der Merker für die stille Umleitung gilt je Tab, damit sie höchstens einmal versucht wird.
  const hintKey = `seed-auth.${config.clientId}.login-hint`;
  const attemptKey = `seed-auth.${config.clientId}.silent-redirect`;
  let redirecting = false;
  let renewal: Promise<string> | null = null;

  function remember(account: AccountInfo) {
    client.setActiveAccount(account);
    writeStorage('localStorage', hintKey, account.username);
  }

  // Erst ein echtes Token beweist eine gültige Sitzung; danach darf wieder still umgeleitet werden.
  function succeeded(account: AccountInfo) {
    remember(account);
    writeStorage('sessionStorage', attemptKey, null);
  }

  async function trySsoSilent(loginHint: string | null) {
    // Ohne Bridge-Seite kann MSAL 5 die Antwort aus dem iframe nicht empfangen; ssoSilent liefe
    // dann immer in den Timeout.
    if (!bridgeUri || !loginHint) {
      return null;
    }
    try {
      const result = await client.ssoSilent({ scopes, loginHint, redirectUri: bridgeUri });
      succeeded(result.account);
      return result.accessToken;
    } catch {
      return null;
    }
  }

  async function redirect(account: AccountInfo | null) {
    redirecting = true;
    const loginHint = account?.username ?? readStorage('localStorage', hintKey);
    // Wer schon einmal angemeldet war, wird zuerst ohne Dialog umgeleitet (prompt=none): Mit
    // Sitzung bei Entra ID kommt die Seite sofort zurück, ohne Sitzung mit einem Fehler statt einer
    // Anmeldeseite. Der zweite Versuch im selben Tab läuft interaktiv.
    const silent = loginHint !== null && readStorage('sessionStorage', attemptKey) === null;
    writeStorage('sessionStorage', attemptKey, '1');
    const request = {
      scopes,
      ...(loginHint ? { loginHint } : {}),
      ...(silent ? { prompt: 'none' } : {}),
    };
    if (account) {
      await client.acquireTokenRedirect({ ...request, account });
    } else {
      await client.loginRedirect(request);
    }
  }

  async function renew(account: AccountInfo): Promise<string> {
    const token = await trySsoSilent(account.username);
    if (token !== null) {
      return token;
    }
    await redirect(account);
    throw new SeedAuthRedirectError();
  }

  try {
    const result = await client.handleRedirectPromise();
    if (result?.account) {
      succeeded(result.account);
    }
  } catch (error) {
    // Eine Umleitung mit prompt=none kommt ohne Sitzung bei Entra ID mit interaction_required
    // zurück. Das ist erwartet; die nächste Anmeldung läuft interaktiv.
    if (!(error instanceof InteractionRequiredAuthError)) {
      throw error;
    }
  }

  const account = client.getActiveAccount() ?? client.getAllAccounts()[0] ?? null;
  if (account) {
    remember(account);
  } else {
    // Neuer Tab oder abgelaufene Sitzung: erst still über die Sitzung bei Entra ID anmelden.
    await trySsoSilent(readStorage('localStorage', hintKey));
  }

  return {
    get account() {
      return client.getActiveAccount();
    },
    login: () => redirect(null),
    logout: () => {
      writeStorage('localStorage', hintKey, null);
      writeStorage('sessionStorage', attemptKey, null);
      return client.logoutRedirect({ account: client.getActiveAccount() ?? undefined });
    },
    async getAccessToken() {
      if (redirecting) {
        throw new SeedAuthRedirectError();
      }

      const active = client.getActiveAccount();
      if (!active) {
        await redirect(null);
        throw new SeedAuthRedirectError();
      }

      try {
        // Ohne iframe: Access-Token aus dem Cache oder per Refresh-Token. Das iframe kommt nur
        // über ssoSilent und die Bridge-Seite zum Einsatz, danach die Umleitung.
        const result = await client.acquireTokenSilent({
          scopes,
          account: active,
          cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
        });
        writeStorage('sessionStorage', attemptKey, null);
        return result.accessToken;
      } catch (error) {
        if (!requiresInteraction(error)) {
          throw error;
        }
      }

      // Parallele Aufrufe teilen sich eine Erneuerung, sonst startete jeder eine eigene Umleitung.
      renewal ??= renew(active).finally(() => {
        renewal = null;
      });
      return renewal;
    },
  };
}

type StorageKind = 'localStorage' | 'sessionStorage';

// Gesperrter Speicher (privates Fenster, Richtlinien) darf die Anmeldung nicht verhindern.
function readStorage(kind: StorageKind, key: string): string | null {
  try {
    return globalThis[kind]?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

function writeStorage(kind: StorageKind, key: string, value: string | null) {
  try {
    if (value === null) {
      globalThis[kind]?.removeItem(key);
    } else {
      globalThis[kind]?.setItem(key, value);
    }
  } catch {
    // ignorieren, siehe readStorage
  }
}
