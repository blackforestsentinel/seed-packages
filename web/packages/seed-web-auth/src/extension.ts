// Anmeldung für Browser-Erweiterungen (Edge, Chrome, Manifest V3). Eine Erweiterung läuft unter
// chrome-extension://<id> statt auf einer Web-Origin: keine Umleitung der ganzen Seite, keine
// Bridge-Seite im iframe, viele kurzlebige Seiten (neuer Tab) und ein Service Worker. Deshalb kein
// MSAL, sondern Autorisierungscode mit PKCE über chrome.identity.launchWebAuthFlow und ein
// gemeinsamer Token-Speicher in chrome.storage. Nach außen dasselbe Interface SeedAuth wie
// createSeedAuth, damit createHttpClient, SeedUserProvider und seed-web-people unverändert laufen.
//
// MSAL wird hier nur als Typ (AccountInfo) genutzt und landet nicht im Bundle der Erweiterung.

import type { AccountInfo } from '@azure/msal-browser';

import type { SeedAuthConfig } from './config.js';
import { SeedAuthInteractionRequiredError, SeedAuthScopeError } from './errors.js';
import type { SeedAuth } from './index.js';

export { isSeedAuthConfig } from './config.js';
export type { SeedAuthConfig } from './config.js';
export { SeedAuthInteractionRequiredError, SeedAuthScopeError } from './errors.js';
export type { SeedAuth } from './index.js';
export { hasCapability, loadSeedUser, roleDisplayNames } from './user.js';
export type { SeedRoleDetail, SeedUser, SeedUserSource } from './user.js';

/** Ausschnitt aus chrome.storage.local bzw. chrome.storage.session. */
export interface SeedExtensionStorageArea {
  get(keys: string | string[]): Promise<Record<string, unknown>>;
  set(items: Record<string, unknown>): Promise<void>;
  remove(keys: string | string[]): Promise<void>;
}

/** Angaben für chrome.identity.launchWebAuthFlow. */
export interface SeedWebAuthFlowDetails {
  url: string;
  interactive: boolean;
  abortOnLoadForNonInteractive?: boolean;
  timeoutMsForNonInteractive?: number;
}

/** Ausschnitt aus chrome.identity und chrome.storage, den die Anmeldung braucht. */
export interface SeedExtensionBrowserApi {
  identity: {
    getRedirectURL(path?: string): string;
    launchWebAuthFlow(details: SeedWebAuthFlowDetails): Promise<string | undefined>;
  };
  storage: {
    local: SeedExtensionStorageArea;
    session: SeedExtensionStorageArea;
    /** Hält account aktuell, wenn sich eine andere Seite der Erweiterung an- oder abmeldet. */
    onChanged?: {
      addListener(callback: (changes: Record<string, { newValue?: unknown }>, areaName: string) => void): void;
    };
  };
}

/** Ausschnitt aus navigator.locks (Web Locks), gilt für alle Seiten und den Service Worker der Erweiterung. */
export interface SeedExtensionLocks {
  request<T>(name: string, callback: () => Promise<T>): Promise<T>;
}

export interface SeedExtensionAuthOptions {
  /**
   * Redirect-URI, Default chrome.identity.getRedirectURL(), also https://<id>.chromiumapp.org/.
   * Sie steht an der SPA-Registrierung (project.yaml: auth.extraRedirectUris).
   */
  redirectUri?: string;
  /** Geht bei login() als domain_hint mit, z. B. example.org; Entra ID zeigt dann gleich den richtigen Tenant. */
  domainHint?: string;
  /**
   * Ablage von Access- und Refresh-Token, gemeinsam für alle Seiten der Erweiterung:
   * - session (Default): chrome.storage.session, nur im Speicher, endet mit dem Browser. Danach
   *   meldet getAccessToken die Person still über ihre Sitzung bei Entra ID wieder an.
   * - local: chrome.storage.local, überdauert Neustarts, solange das Refresh-Token gilt (24 Stunden).
   *   Es liegt dann unverschlüsselt auf der Platte und ist für die Content Scripts der Erweiterung
   *   lesbar; nur nehmen, wenn die stille Anmeldung nach einem Neustart nicht reicht.
   * Beim Start löscht createSeedExtensionAuth die Tokens aus dem jeweils anderen Bereich.
   */
  tokenStorage?: 'session' | 'local';
  /** Ersatz für chrome.identity und chrome.storage, etwa in Tests. Default globalThis.chrome. */
  browser?: SeedExtensionBrowserApi;
  /** Ersatz für fetch, etwa in Tests. */
  fetch?: typeof fetch;
  /** Ersatz für navigator.locks, etwa in Tests. Ohne Web Locks bündelt nur jede Seite für sich. */
  locks?: SeedExtensionLocks;
  /** Uhr in Millisekunden, für Tests. */
  now?: () => number;
}

/** Gespeicherte Tokens; das Refresh-Token gilt für alle Berechtigungen, Access-Tokens je Scope-Satz. */
interface StoredTokens {
  refreshToken?: string;
  accessTokens: Record<string, { token: string; expiresOn: number }>;
}

/** Code aus launchWebAuthFlow samt dem, was zum Einlösen gehört. */
interface AuthorizationCode {
  scopes: string[];
  code: string;
  verifier: string;
  nonce: string;
}

/** Warum es ohne Fenster kein Token gab: keine Sitzung bei Entra ID oder fehlende Einwilligung. */
type Blocked = 'login' | 'consent';

/** Antwort des Token-Endpunkts bzw. Fehler im Redirect. */
interface TokenResponse {
  access_token?: string;
  expires_in?: number | string;
  refresh_token?: string;
  id_token?: string;
  error?: string;
  error_description?: string;
  error_codes?: number[];
  suberror?: string;
}

/** Fehler von Entra ID, die nur eine (stille oder interaktive) Anmeldung behebt. */
const INTERACTION_ERRORS = new Set(['invalid_grant', 'interaction_required', 'login_required', 'consent_required', 'account_selection_required', 'access_denied']);

/** Access-Tokens gelten bis kurz vor Ablauf, wie bei MSAL. */
const EXPIRY_OFFSET_MS = 5 * 60 * 1000;

/** Fehler von Entra ID samt Code, z. B. invalid_grant. */
class EntraError extends Error {
  readonly error: string;
  readonly codes: number[];
  readonly suberror: string | undefined;

  constructor(response: TokenResponse, context: string) {
    super(`${context}: ${response.error}${response.error_description ? ` (${response.error_description.split(/\r?\n/)[0]})` : ''}`);
    this.name = 'EntraError';
    this.error = response.error ?? 'unknown_error';
    this.codes = Array.isArray(response.error_codes) ? response.error_codes : [];
    this.suberror = response.suberror;
  }

  get interactionRequired(): boolean {
    return INTERACTION_ERRORS.has(this.error);
  }

  /** Fehlende Einwilligung (AADSTS65001) oder verweigert. */
  get consent(): boolean {
    return this.error === 'consent_required' || this.error === 'access_denied' || this.suberror === 'consent_required' || this.codes.includes(65001);
  }
}

/**
 * Anmeldung für eine Browser-Erweiterung mit demselben Interface wie createSeedAuth. account kommt
 * sofort aus chrome.storage.local, ohne Netz und ohne Token.
 *
 * getAccessToken öffnet nie ein Fenster: erst Access-Token aus dem Speicher oder per Refresh-Token,
 * dann still per launchWebAuthFlow mit prompt=none über die Sitzung bei Entra ID, sonst
 * SeedAuthInteractionRequiredError. Nur login() öffnet ein Anmeldefenster; die App ruft es nach
 * einem Klick auf. redirectBridgePath aus der Konfiguration gilt hier nicht.
 */
export async function createSeedExtensionAuth(config: SeedAuthConfig, options: SeedExtensionAuthOptions = {}): Promise<SeedAuth> {
  const browser = options.browser ?? (globalThis as { chrome?: SeedExtensionBrowserApi }).chrome;
  if (!browser?.identity || !browser.storage) {
    throw new Error('seed-web-auth/extension: chrome.identity und chrome.storage fehlen. Braucht die Erweiterung die Berechtigungen identity und storage?');
  }
  const fetchFn = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => fetch(input, init));
  const locks = options.locks ?? (globalThis.navigator as { locks?: SeedExtensionLocks } | undefined)?.locks;
  const now = options.now ?? Date.now;
  const redirectUri = options.redirectUri ?? browser.identity.getRedirectURL();
  const authority = `https://login.microsoftonline.com/${config.tenantId}/oauth2/v2.0`;
  const apiScopes = [config.apiScope];
  const apiKey = scopeKey(apiScopes);

  // Konto in local, damit es Neustarts überdauert und auch der Service Worker den login_hint kennt.
  // Tokens je nach Option; die Merker, was ohne Fenster nicht ging, gelten je Browser-Sitzung, weil
  // ständig neue Tabs entstehen.
  const accountKey = `seed-auth.${config.clientId}.account`;
  const tokensKey = `seed-auth.${config.clientId}.tokens`;
  const blockedKey = `seed-auth.${config.clientId}.blocked`;
  const lockName = `seed-auth.${config.clientId}`;
  const tokenArea = options.tokenStorage === 'local' ? browser.storage.local : browser.storage.session;
  const otherTokenArea = options.tokenStorage === 'local' ? browser.storage.session : browser.storage.local;

  // Tokens nur im gewählten Bereich: Nach einem Wechsel von local zurück auf session bliebe sonst
  // ein Refresh-Token auf der Platte liegen.
  const [stored] = await Promise.all([browser.storage.local.get(accountKey), otherTokenArea.remove(tokensKey)]);
  let account = parseAccount(stored[accountKey]);
  const pending = new Map<string, Promise<string>>();

  browser.storage.onChanged?.addListener((changes, areaName) => {
    if (areaName === 'local' && accountKey in changes) {
      account = parseAccount(changes[accountKey]?.newValue);
    }
  });

  async function readTokens(): Promise<StoredTokens> {
    const stored = (await tokenArea.get(tokensKey))[tokensKey] as Partial<StoredTokens> | undefined;
    return { refreshToken: stored?.refreshToken, accessTokens: { ...stored?.accessTokens } };
  }

  async function readBlocked(): Promise<Record<string, Blocked>> {
    return { ...((await browser!.storage.session.get(blockedKey))[blockedKey] as Record<string, Blocked> | undefined) };
  }

  async function setBlocked(key: string, reason: Blocked | null) {
    const blocked = await readBlocked();
    if (reason === null) {
      if (!(key in blocked)) {
        return;
      }
      delete blocked[key];
    } else {
      blocked[key] = reason;
    }
    await browser!.storage.session.set({ [blockedKey]: blocked });
  }

  function cached(tokens: StoredTokens, key: string): string | null {
    const entry = tokens.accessTokens[key];
    return entry && entry.expiresOn - EXPIRY_OFFSET_MS > now() ? entry.token : null;
  }

  /** Speichert eine Antwort des Token-Endpunkts; mit replace ersetzt sie alles (Anmeldung). */
  async function store(response: TokenResponse, key: string, nonce: string | null, replace: boolean): Promise<string> {
    if (!response.access_token) {
      throw new Error('Token-Endpunkt: Antwort ohne access_token.');
    }
    if (response.id_token) {
      const next = accountFromIdToken(response.id_token, config.clientId, nonce);
      if (next.homeAccountId !== account?.homeAccountId || next.username !== account.username || next.name !== account.name) {
        account = next;
        await browser!.storage.local.set({ [accountKey]: next });
      }
    }
    const tokens: StoredTokens = replace ? { accessTokens: {} } : await readTokens();
    tokens.accessTokens[key] = { token: response.access_token, expiresOn: now() + Number(response.expires_in ?? 3600) * 1000 };
    if (response.refresh_token) {
      tokens.refreshToken = response.refresh_token;
    }
    await tokenArea.set({ [tokensKey]: tokens });
    return response.access_token;
  }

  async function tokenRequest(body: Record<string, string>): Promise<TokenResponse> {
    // Der Browser schickt Origin: chrome-extension://<id> mit; deshalb gilt die Einlösung als
    // SPA-Anfrage, und die Redirect-URI muss an der SPA-Plattform stehen. Netzwerkfehler gehen
    // unverändert weiter: Dann ist die Sitzung nicht verloren, und eine Anmeldung hilft nicht.
    const response = await fetchFn(`${authority}/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ client_id: config.clientId, ...body }).toString(),
    });
    let result: TokenResponse;
    try {
      result = (await response.json()) as TokenResponse;
    } catch {
      throw new Error(`Token-Endpunkt: keine gültige Antwort (HTTP ${response.status}).`);
    }
    if (!response.ok || result.error) {
      if (!result.error || response.status >= 500 || response.status === 429) {
        throw new Error(`Token-Endpunkt: HTTP ${response.status}${result.error ? ` ${result.error}` : ''}.`);
      }
      throw new EntraError(result, 'Token-Endpunkt');
    }
    return result;
  }

  /** Autorisierungscode mit PKCE über launchWebAuthFlow; einlösen muss ihn danach redeem. */
  async function authorize(scopes: string[], interactive: boolean): Promise<AuthorizationCode> {
    const verifier = randomString(32);
    const state = randomString(16);
    const nonce = randomString(16);
    const loginHint = account?.username;
    const params = new URLSearchParams({
      client_id: config.clientId,
      response_type: 'code',
      redirect_uri: redirectUri,
      // fragment: Der Code bleibt im Browser, auch falls die Redirect-URI doch geladen würde.
      response_mode: 'fragment',
      scope: requestScopes(scopes),
      state,
      nonce,
      code_challenge: await sha256(verifier),
      code_challenge_method: 'S256',
      ...(loginHint ? { login_hint: loginHint } : {}),
      ...(interactive ? (options.domainHint ? { domain_hint: options.domainHint } : {}) : { prompt: 'none' }),
    });
    const url = `${authority}/authorize?${params}`;

    let responseUrl: string | undefined;
    try {
      responseUrl = await browser!.identity.launchWebAuthFlow(
        interactive
          ? { url, interactive: true }
          : // Entra ID leitet bei prompt=none teils erst per Skript weiter; ohne diese Angaben brächte
            // Chromium den unsichtbaren Fluss nach dem ersten Laden ab.
            { url, interactive: false, abortOnLoadForNonInteractive: false, timeoutMsForNonInteractive: 10_000 },
      );
    } catch (error) {
      // Offline gilt wie beim Token-Endpunkt als vorübergehend.
      if (globalThis.navigator?.onLine === false || interactive) {
        throw error;
      }
      throw new EntraError({ error: 'interaction_required', error_description: String((error as Error)?.message ?? error) }, 'Anmeldung ohne Fenster');
    }

    if (!responseUrl) {
      throw new EntraError({ error: 'interaction_required', error_description: 'launchWebAuthFlow ohne Antwort' }, 'Anmeldung');
    }
    const result = parseRedirect(responseUrl);
    if (result.get('state') !== state) {
      throw new Error('Anmeldung: Antwort mit unbekanntem state.');
    }
    const code = result.get('code');
    if (!code) {
      throw new EntraError(
        { error: result.get('error') ?? 'invalid_response', error_description: result.get('error_description') ?? undefined, suberror: result.get('suberror') ?? undefined },
        'Anmeldung',
      );
    }
    return { scopes, code, verifier, nonce };
  }

  /** Löst den Code ein und speichert die Tokens; mit replace (Anmeldung) ersetzen sie alle bisherigen. */
  async function redeem({ scopes, code, verifier, nonce }: AuthorizationCode, replace: boolean): Promise<string> {
    const response = await tokenRequest({
      grant_type: 'authorization_code',
      code,
      redirect_uri: redirectUri,
      code_verifier: verifier,
      scope: requestScopes(scopes),
    });
    return store(response, scopeKey(scopes), nonce, replace);
  }

  function withLock<T>(run: () => Promise<T>): Promise<T> {
    return locks ? locks.request(lockName, run) : run();
  }

  async function acquire(scopes: string[], key: string): Promise<string> {
    const fast = cached(await readTokens(), key);
    if (fast) {
      return fast;
    }
    // Ein Lock für alle Seiten der Erweiterung: Zwei neue Tabs erneuern nicht doppelt, und das
    // Refresh-Token wird nicht parallel eingelöst.
    return withLock(() => renew(scopes, key));
  }

  async function renew(scopes: string[], key: string): Promise<string> {
    // Eine andere Seite hat inzwischen erneuert.
    const tokens = await readTokens();
    const token = cached(tokens, key);
    if (token) {
      return token;
    }
    if (!account) {
      throw new SeedAuthInteractionRequiredError();
    }

    // 1. Refresh-Token.
    if (tokens.refreshToken) {
      try {
        const response = await tokenRequest({ grant_type: 'refresh_token', refresh_token: tokens.refreshToken, scope: requestScopes(scopes) });
        const result = await store(response, key, null, false);
        await setBlocked(key, null);
        return result;
      } catch (error) {
        if (!(error instanceof EntraError && error.interactionRequired)) {
          throw error;
        }
        // Abgelaufen oder widerrufen: Die anderen Seiten sollen es nicht noch einmal versuchen.
        // Fehlt nur die Einwilligung für diese Scopes, gilt es für die API weiter.
        if (!error.consent) {
          await tokenArea.set({ [tokensKey]: { accessTokens: tokens.accessTokens } });
        }
      }
    }

    // 2. Still über die Sitzung bei Entra ID, je Scope-Satz höchstens einmal je Browser-Sitzung.
    const blocked = (await readBlocked())[key];
    if (blocked) {
      throw blockedError(scopes, key, blocked);
    }
    try {
      const result = await redeem(await authorize(scopes, false), false);
      await setBlocked(key, null);
      return result;
    } catch (error) {
      if (!(error instanceof EntraError && error.interactionRequired)) {
        throw error;
      }
      // 3. Kein Fenster: Die App bietet die Anmeldung an.
      const reason: Blocked = error.consent ? 'consent' : 'login';
      await setBlocked(key, reason);
      throw blockedError(scopes, key, reason, error);
    }
  }

  function blockedError(scopes: string[], key: string, reason: Blocked, cause?: unknown) {
    // Für zusätzliche Berechtigungen (etwa Graph) gilt wie im Web SeedAuthScopeError, wenn nur die
    // Einwilligung fehlt; eine neue Anmeldung änderte daran nichts.
    return key !== apiKey && reason === 'consent' ? new SeedAuthScopeError(scopes, { cause }) : new SeedAuthInteractionRequiredError({ cause });
  }

  return {
    get account() {
      return account;
    },
    async login() {
      // Das Fenster bleibt offen, solange die Person braucht; erst das Einlösen hält die anderen
      // Seiten kurz auf, damit keine gleichzeitige Erneuerung die neuen Tokens überschreibt.
      const code = await authorize(apiScopes, true);
      await withLock(async () => {
        await redeem(code, true);
        await browser.storage.session.remove(blockedKey);
      });
    },
    async logout() {
      // Abmelden nur in der Erweiterung; die Sitzung bei Entra ID gehört dem Browser-Profil. Im
      // Lock, damit eine laufende Erneuerung Konto und Tokens danach nicht wieder schreibt.
      account = null;
      await withLock(async () => {
        account = null;
        await Promise.all([
          browser.storage.local.remove([accountKey, tokensKey]),
          browser.storage.session.remove([blockedKey, tokensKey]),
        ]);
      });
    },
    getAccessToken(requested?: string[]) {
      // Auch als Callback ohne Argument nutzbar (createHttpClient); alles andere als eine Liste
      // von Scopes heißt API.
      const scopes = Array.isArray(requested) && requested.length > 0 ? requested : apiScopes;
      const key = scopeKey(scopes);
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

/** Angefragte Scopes plus OpenID (Konto) und offline_access (Refresh-Token). */
function requestScopes(scopes: readonly string[]): string {
  return [...new Set([...scopes, 'openid', 'profile', 'offline_access'])].join(' ');
}

function parseRedirect(url: string): URLSearchParams {
  const parsed = new URL(url);
  return new URLSearchParams(parsed.hash.length > 1 ? parsed.hash.slice(1) : parsed.search);
}

/**
 * Konto aus dem ID-Token. Das Token kommt direkt vom Token-Endpunkt über TLS; dann darf die Prüfung
 * der Signatur entfallen (OpenID Connect Core 3.1.3.7). Es dient nur der Anzeige und dem login_hint,
 * die API prüft ihre Access-Tokens selbst.
 */
function accountFromIdToken(idToken: string, clientId: string, nonce: string | null): AccountInfo {
  let claims: Record<string, unknown>;
  try {
    claims = JSON.parse(base64UrlDecode(idToken.split('.')[1] ?? '')) as Record<string, unknown>;
  } catch {
    throw new Error('ID-Token nicht lesbar.');
  }
  if (claims.aud !== clientId) {
    throw new Error('ID-Token für eine andere Anwendung.');
  }
  if (nonce !== null && claims.nonce !== nonce) {
    throw new Error('ID-Token mit falscher nonce.');
  }
  const oid = String(claims.oid ?? claims.sub ?? '');
  const tid = String(claims.tid ?? '');
  const username = String(claims.preferred_username ?? claims.upn ?? claims.email ?? '');
  const name = typeof claims.name === 'string' ? claims.name : undefined;
  return {
    homeAccountId: `${oid}.${tid}`,
    environment: 'login.microsoftonline.com',
    tenantId: tid,
    username,
    localAccountId: oid,
    name,
    idTokenClaims: { oid, tid, preferred_username: username, ...(name ? { name } : {}) },
  };
}

function parseAccount(value: unknown): AccountInfo | null {
  const account = value as Partial<AccountInfo> | null | undefined;
  return typeof account?.homeAccountId === 'string' && typeof account.username === 'string' ? (account as AccountInfo) : null;
}

function randomString(bytes: number): string {
  return base64UrlEncode(crypto.getRandomValues(new Uint8Array(bytes)));
}

async function sha256(value: string): Promise<string> {
  return base64UrlEncode(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value))));
}

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function base64UrlDecode(value: string): string {
  const binary = atob(value.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(value.length / 4) * 4, '='));
  return new TextDecoder().decode(Uint8Array.from(binary, (c) => c.charCodeAt(0)));
}
