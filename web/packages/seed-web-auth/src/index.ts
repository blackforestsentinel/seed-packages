import {
  AuthError,
  BrowserAuthErrorCodes,
  CacheLookupPolicy,
  createStandardPublicClientApplication,
  InteractionRequiredAuthError,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser';

import type { SeedDirectoryConfig, SeedLocalDirectoryConfig } from './directory.js';

export { hasCapability, loadSeedUser, roleDisplayNames } from './user.js';
export type { SeedRoleDetail, SeedUser, SeedUserSource } from './user.js';
export type { SeedDirectoryConfig, SeedLocalDirectoryConfig, SeedLocalPerson } from './directory.js';

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
  /**
   * Access-Token für die API; passt zu getAccessToken von createHttpClient (seed-web-core). Mit
   * scopes ein Token für genau diese Berechtigungen, etwa für Microsoft Graph
   * (https://graph.microsoft.com/User.ReadBasic.All). Parallele Aufrufe je Scope-Satz teilen sich
   * eine Anfrage.
   */
  getAccessToken(scopes?: string[]): Promise<string>;
}

/** Wird geworfen, wenn für ein Token eine Anmeldung nötig ist und die Seite dafür umleitet. */
export class SeedAuthRedirectError extends Error {
  constructor() {
    super('Anmeldung nötig, die Seite wird umgeleitet.');
    this.name = 'SeedAuthRedirectError';
  }
}

/**
 * Wird geworfen, wenn es für zusätzliche Berechtigungen (etwa Graph) kein Token gibt und die
 * Umleitung dafür in diesem Tab schon still und interaktiv versucht wurde. Meist fehlt die
 * Einwilligung, die die Person nicht selbst erteilen darf; eine weitere Umleitung änderte nichts.
 */
export class SeedAuthScopeError extends Error {
  readonly scopes: string[];

  constructor(scopes: string[], options?: { cause?: unknown }) {
    super(`Kein Token für ${scopes.join(' ')}: Entra ID verlangt eine Anmeldung oder Einwilligung, die in diesem Tab schon versucht wurde.`, options);
    this.name = 'SeedAuthScopeError';
    this.scopes = scopes;
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
  const apiScopes = [config.apiScope];
  const apiKey = scopeKey(apiScopes);

  // Der Benutzername als login_hint überdauert den Tab (die Tokens liegen nur im sessionStorage);
  // der Merker für die stille Umleitung gilt je Tab, damit sie höchstens einmal versucht wird.
  const hintKey = `seed-auth.${config.clientId}.login-hint`;
  const attemptKey = `seed-auth.${config.clientId}.silent-redirect`;
  // Zusätzliche Berechtigungen (etwa Graph) haben je Scope-Satz einen eigenen Merker. Ein Token für
  // die API beweist nichts über ihre Einwilligung; ein gemeinsamer Merker ließe die Seite ohne
  // Einwilligung immer wieder umleiten.
  const scopeAttemptPrefix = `seed-auth.${config.clientId}.scopes.`;
  const scopeRedirectKey = `seed-auth.${config.clientId}.scope-redirect`;
  let redirecting = false;
  const pending = new Map<string, Promise<string>>();

  function attemptKeyFor(key: string) {
    return key === apiKey ? attemptKey : scopeAttemptPrefix + key;
  }

  function remember(account: AccountInfo) {
    client.setActiveAccount(account);
    writeStorage('localStorage', hintKey, account.username);
  }

  // Erst ein echtes Token beweist eine gültige Sitzung; danach darf wieder still umgeleitet werden.
  function succeeded(account: AccountInfo, key = apiKey) {
    remember(account);
    writeStorage('sessionStorage', attemptKeyFor(key), null);
  }

  // Die Umleitung für diese zusätzlichen Berechtigungen lief in diesem Tab schon interaktiv.
  function gaveUp(key: string) {
    return key !== apiKey && readStorage('sessionStorage', attemptKeyFor(key)) === 'interactive';
  }

  async function trySsoSilent(loginHint: string | null, scopes = apiScopes) {
    // Ohne Bridge-Seite kann MSAL 5 die Antwort aus dem iframe nicht empfangen; ssoSilent liefe
    // dann immer in den Timeout.
    if (!bridgeUri || !loginHint) {
      return null;
    }
    try {
      const result = await client.ssoSilent({ scopes, loginHint, redirectUri: bridgeUri });
      succeeded(result.account, scopeKey(scopes));
      return result.accessToken;
    } catch {
      return null;
    }
  }

  async function redirect(account: AccountInfo | null, scopes = apiScopes) {
    const key = scopeKey(scopes);
    const loginHint = account?.username ?? readStorage('localStorage', hintKey);
    // Wer schon einmal angemeldet war, wird zuerst ohne Dialog umgeleitet (prompt=none): Mit
    // Sitzung bei Entra ID kommt die Seite sofort zurück, ohne Sitzung mit einem Fehler statt einer
    // Anmeldeseite. Der zweite Versuch im selben Tab läuft interaktiv.
    const silent = loginHint !== null && readStorage('sessionStorage', attemptKeyFor(key)) === null;
    if (key === apiKey) {
      writeStorage('sessionStorage', attemptKey, '1');
    } else {
      // Für zusätzliche Berechtigungen gibt es danach keinen dritten Versuch (gaveUp): Ohne
      // Einwilligung käme die Seite sonst bei jedem Start wieder zu Entra ID.
      writeStorage('sessionStorage', attemptKeyFor(key), silent ? 'silent' : 'interactive');
      writeStorage('sessionStorage', scopeRedirectKey, '1');
    }
    redirecting = true;
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

  async function renew(account: AccountInfo, scopes: string[], key: string, cause: unknown): Promise<string> {
    if (gaveUp(key)) {
      throw new SeedAuthScopeError(scopes, { cause });
    }
    const token = await trySsoSilent(account.username, scopes);
    if (token !== null) {
      return token;
    }
    await redirect(account, scopes);
    throw new SeedAuthRedirectError();
  }

  async function acquire(scopes: string[], key: string): Promise<string> {
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
      writeStorage('sessionStorage', attemptKeyFor(key), null);
      return result.accessToken;
    } catch (error) {
      if (!requiresInteraction(error)) {
        throw error;
      }
      return renew(active, scopes, key, error);
    }
  }

  try {
    const result = await client.handleRedirectPromise();
    if (result?.account) {
      succeeded(result.account);
    }
  } catch (error) {
    // Eine Umleitung mit prompt=none kommt ohne Sitzung bei Entra ID mit interaction_required
    // zurück. Das ist erwartet; die nächste Anmeldung läuft interaktiv. Eine Umleitung für
    // zusätzliche Berechtigungen kommt ohne Einwilligung auch mit access_denied zurück; die App
    // startet dann ohne diese Berechtigungen.
    if (!(error instanceof InteractionRequiredAuthError)) {
      if (readStorage('sessionStorage', scopeRedirectKey) === null) {
        throw error;
      }
      console.warn('seed-web-auth: Keine Einwilligung für zusätzliche Berechtigungen.', error);
    }
  } finally {
    writeStorage('sessionStorage', scopeRedirectKey, null);
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
      clearStorage('sessionStorage', scopeAttemptPrefix);
      return client.logoutRedirect({ account: client.getActiveAccount() ?? undefined });
    },
    getAccessToken(requested?: string[]) {
      // Auch als Callback ohne Argument nutzbar (createHttpClient); alles andere als eine Liste
      // von Scopes heißt API.
      const scopes = Array.isArray(requested) && requested.length > 0 ? requested : apiScopes;
      const key = scopeKey(scopes);
      // Parallele Aufrufe je Scope-Satz teilen sich eine Anfrage und damit auch eine Erneuerung,
      // sonst startete jeder eine eigene Umleitung.
      let token = pending.get(key);
      if (!token) {
        token = acquire(scopes, key).finally(() => pending.delete(key));
        pending.set(key, token);
      }
      return token;
    },
  };
}

/** Gleiche Scopes in anderer Reihenfolge sind derselbe Satz. */
function scopeKey(scopes: readonly string[]): string {
  return [...new Set(scopes)].sort().join(' ');
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

function clearStorage(kind: StorageKind, prefix: string) {
  try {
    const storage = globalThis[kind];
    for (let i = (storage?.length ?? 0) - 1; i >= 0; i--) {
      const key = storage?.key(i);
      if (key?.startsWith(prefix)) {
        storage?.removeItem(key);
      }
    }
  } catch {
    // ignorieren, siehe readStorage
  }
}
