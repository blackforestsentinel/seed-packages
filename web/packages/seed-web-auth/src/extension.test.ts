import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createSeedExtensionAuth,
  SeedAuthInteractionRequiredError,
  SeedAuthScopeError,
  type SeedExtensionAuthOptions,
  type SeedExtensionBrowserApi,
  type SeedExtensionLocks,
  type SeedExtensionStorageArea,
  type SeedWebAuthFlowDetails,
} from './extension.js';

const config = { clientId: 'spa-id', tenantId: 'tenant-id', apiScope: 'api://api-id/access_as_user', redirectBridgePath: '/redirect.html' };
const redirectUri = 'https://abcdefghijklmnopabcdefghijklmnop.chromiumapp.org/';
const graphScopes = ['https://graph.microsoft.com/User.Read', 'https://graph.microsoft.com/User.ReadBasic.All'];
const person = { oid: 'oid-1', tid: 'tenant-id', name: 'Erika Muster', preferred_username: 'erika@example.org' };
const accountKey = 'seed-auth.spa-id.account';
const tokensKey = 'seed-auth.spa-id.tokens';
const blockedKey = 'seed-auth.spa-id.blocked';
const oidcScopes = new Set(['openid', 'profile', 'offline_access']);

type Listener = (changes: Record<string, { newValue?: unknown }>, areaName: string) => void;

function storageArea(name: string, listeners: Listener[]) {
  const data = new Map<string, unknown>();
  const notify = (changes: Record<string, { newValue?: unknown }>) => listeners.forEach((listener) => listener(changes, name));
  const area: SeedExtensionStorageArea = {
    async get(keys) {
      const list = Array.isArray(keys) ? keys : [keys];
      return Object.fromEntries(list.filter((key) => data.has(key)).map((key) => [key, structuredClone(data.get(key))]));
    },
    async set(items) {
      const changes: Record<string, { newValue?: unknown }> = {};
      for (const [key, value] of Object.entries(items)) {
        data.set(key, structuredClone(value));
        changes[key] = { newValue: structuredClone(value) };
      }
      notify(changes);
    },
    async remove(keys) {
      const changes: Record<string, { newValue?: unknown }> = {};
      for (const key of Array.isArray(keys) ? keys : [keys]) {
        if (data.delete(key)) {
          changes[key] = {};
        }
      }
      notify(changes);
    },
  };
  return { area, data };
}

/** Mutex je Name wie navigator.locks, gemeinsam für alle „Tabs“ eines Tests. */
function fakeLocks(): SeedExtensionLocks {
  const tails = new Map<string, Promise<unknown>>();
  return {
    request(name, callback) {
      const result = (tails.get(name) ?? Promise.resolve()).then(() => callback());
      tails.set(name, result.catch(() => {}));
      return result;
    },
  };
}

async function sha256(value: string) {
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)));
  return btoa(String.fromCharCode(...hash)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function jwt(claims: Record<string, unknown>) {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'RS256' })}.${encode(claims)}.signatur`;
}

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

/**
 * Entra ID im Kleinen: Autorisierung samt PKCE, Einwilligung je Scope, Refresh-Tokens und die
 * Sitzung im Browser-Profil, dazu chrome.identity, chrome.storage (Sitzung und lokal) und Uhr.
 */
function setup() {
  const entra = {
    /** Sitzung bei Entra ID im Browser-Profil; ohne sie scheitert prompt=none. */
    session: true,
    consent: new Set([config.apiScope]),
    refreshTokens: new Set<string>(),
    codes: new Map<string, { challenge: string; nonce: string; redirectUri: string; scopes: string[] }>(),
    offline: false,
    /** Antwort auf das Fenster ersetzen, etwa für einen fremden state. */
    tamper: null as null | ((url: string) => string),
    nonce: null as null | string,
    counter: 0,
  };
  const tokenCalls: URLSearchParams[] = [];
  let time = 1_000_000;

  function issue(scopes: string[], nonce?: string) {
    const n = ++entra.counter;
    const refreshToken = `rt-${n}`;
    entra.refreshTokens.add(refreshToken);
    return {
      access_token: `at-${n}:${scopes.join(',')}`,
      expires_in: 3600,
      refresh_token: refreshToken,
      id_token: jwt({ aud: config.clientId, ...person, ...(nonce ? { nonce: entra.nonce ?? nonce } : {}) }),
    };
  }

  const launchWebAuthFlow = vi.fn(async (details: SeedWebAuthFlowDetails): Promise<string | undefined> => {
    const params = new URL(details.url).searchParams;
    const back = (values: Record<string, string>) => {
      const url = `${params.get('redirect_uri')}#${new URLSearchParams({ ...values, state: params.get('state') ?? '' })}`;
      return entra.tamper ? entra.tamper(url) : url;
    };
    if (!details.interactive && !entra.session) {
      return back({ error: 'login_required', error_description: 'AADSTS50058: A silent sign-in request was sent but no user is signed in.' });
    }
    const scopes = (params.get('scope') ?? '').split(' ').filter((scope) => !oidcScopes.has(scope));
    if (scopes.some((scope) => !entra.consent.has(scope))) {
      return back(
        details.interactive
          ? { error: 'access_denied', error_description: 'AADSTS65004: User declined to consent.' }
          : { error: 'consent_required', error_description: 'AADSTS65001: The user or administrator has not consented.' },
      );
    }
    entra.session = true;
    const code = `code-${++entra.counter}`;
    entra.codes.set(code, {
      challenge: params.get('code_challenge') ?? '',
      nonce: params.get('nonce') ?? '',
      redirectUri: params.get('redirect_uri') ?? '',
      scopes,
    });
    return back({ code });
  });

  const fetchFn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    expect(String(input)).toBe('https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token');
    if (entra.offline) {
      throw new TypeError('Failed to fetch');
    }
    const body = new URLSearchParams(String(init?.body));
    tokenCalls.push(body);
    const scopes = (body.get('scope') ?? '').split(' ').filter((scope) => !oidcScopes.has(scope));
    if (body.get('grant_type') === 'authorization_code') {
      const entry = entra.codes.get(body.get('code') ?? '');
      entra.codes.delete(body.get('code') ?? '');
      if (!entry || entry.challenge !== (await sha256(body.get('code_verifier') ?? '')) || entry.redirectUri !== body.get('redirect_uri')) {
        return json(400, { error: 'invalid_grant', error_description: 'AADSTS501481: The Code_Verifier does not match.' });
      }
      return json(200, issue(scopes, entry.nonce));
    }
    if (!entra.refreshTokens.has(body.get('refresh_token') ?? '')) {
      return json(400, { error: 'invalid_grant', error_codes: [700084], error_description: 'AADSTS700084: The refresh token was issued to a single page app (SPA).' });
    }
    if (scopes.some((scope) => !entra.consent.has(scope))) {
      return json(400, { error: 'invalid_grant', suberror: 'consent_required', error_codes: [65001], error_description: 'AADSTS65001: not consented' });
    }
    return json(200, issue(scopes));
  });

  const listeners: Listener[] = [];
  const local = storageArea('local', listeners);
  let session = storageArea('session', listeners);
  const browser: SeedExtensionBrowserApi = {
    identity: { getRedirectURL: () => redirectUri, launchWebAuthFlow },
    storage: {
      local: local.area,
      get session() {
        return session.area;
      },
      onChanged: { addListener: (listener) => void listeners.push(listener) },
    },
  };
  const locks = fakeLocks();

  return {
    entra,
    tokenCalls,
    launchWebAuthFlow,
    fetch: fetchFn,
    local: local.data,
    get session() {
      return session.data;
    },
    advance(ms: number) {
      time += ms;
    },
    /** Browser-Neustart: chrome.storage.session ist leer. */
    restart() {
      session = storageArea('session', listeners);
    },
    /** Eine Seite der Erweiterung, etwa ein neuer Tab. */
    tab(options: SeedExtensionAuthOptions = {}) {
      return createSeedExtensionAuth(config, { browser, fetch: fetchFn, locks, now: () => time, domainHint: 'example.org', ...options });
    },
  };
}

const hours = (n: number) => n * 60 * 60 * 1000;
const grants = (calls: URLSearchParams[]) => calls.map((call) => call.get('grant_type'));
const flows = (mock: ReturnType<typeof setup>['launchWebAuthFlow']) => mock.mock.calls.map(([details]) => details);

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('createSeedExtensionAuth', () => {
  it('meldet nur per login() mit Fenster an und merkt sich das Konto', async () => {
    const t = setup();
    const auth = await t.tab();

    expect(auth.account).toBeNull();
    await expect(auth.getAccessToken()).rejects.toBeInstanceOf(SeedAuthInteractionRequiredError);
    expect(t.launchWebAuthFlow).not.toHaveBeenCalled();

    await auth.login();

    const [details] = flows(t.launchWebAuthFlow);
    expect(details?.interactive).toBe(true);
    const params = new URL(details!.url).searchParams;
    expect(`${new URL(details!.url).origin}${new URL(details!.url).pathname}`).toBe('https://login.microsoftonline.com/tenant-id/oauth2/v2.0/authorize');
    expect(params.get('client_id')).toBe('spa-id');
    expect(params.get('response_type')).toBe('code');
    expect(params.get('redirect_uri')).toBe(redirectUri);
    expect(params.get('code_challenge_method')).toBe('S256');
    expect(params.get('domain_hint')).toBe('example.org');
    expect(params.get('prompt')).toBeNull();
    expect(params.get('login_hint')).toBeNull();
    expect(params.get('scope')?.split(' ')).toEqual([config.apiScope, 'openid', 'profile', 'offline_access']);

    expect(auth.account).toMatchObject({
      homeAccountId: 'oid-1.tenant-id',
      localAccountId: 'oid-1',
      tenantId: 'tenant-id',
      username: 'erika@example.org',
      name: 'Erika Muster',
      idTokenClaims: { oid: 'oid-1' },
    });
    expect(t.local.get(accountKey)).toMatchObject({ username: 'erika@example.org' });
    expect(t.session.has(tokensKey)).toBe(true);
    expect(t.local.has(tokensKey)).toBe(false);

    await expect(auth.getAccessToken()).resolves.toBe(`at-2:${config.apiScope}`);
    expect(grants(t.tokenCalls)).toEqual(['authorization_code']);
  });

  it('kennt das Konto in einem neuen Tab sofort, ohne Netz', async () => {
    const t = setup();
    await (await t.tab()).login();

    const tab = await t.tab();

    expect(tab.account?.username).toBe('erika@example.org');
    await expect(tab.getAccessToken()).resolves.toBe(`at-2:${config.apiScope}`);
    expect(t.tokenCalls).toHaveLength(1);
  });

  it('erneuert per Refresh-Token ohne Fenster', async () => {
    const t = setup();
    const auth = await t.tab();
    await auth.login();
    t.advance(hours(2));

    await expect(auth.getAccessToken()).resolves.toBe(`at-3:${config.apiScope}`);

    expect(grants(t.tokenCalls)).toEqual(['authorization_code', 'refresh_token']);
    expect(t.tokenCalls[1]?.get('refresh_token')).toBe('rt-2');
    expect(t.launchWebAuthFlow).toHaveBeenCalledTimes(1);
  });

  it('meldet nach Ablauf des Refresh-Tokens still über die Sitzung bei Entra ID an', async () => {
    const t = setup();
    const auth = await t.tab();
    await auth.login();
    t.entra.refreshTokens.clear();
    t.advance(hours(25));

    await expect(auth.getAccessToken()).resolves.toMatch(/^at-\d+:api:/);

    const silent = flows(t.launchWebAuthFlow)[1]!;
    expect(silent).toMatchObject({ interactive: false, abortOnLoadForNonInteractive: false });
    const params = new URL(silent.url).searchParams;
    expect(params.get('prompt')).toBe('none');
    expect(params.get('login_hint')).toBe('erika@example.org');
    expect(params.get('domain_hint')).toBeNull();
  });

  it('meldet nach einem Browser-Neustart still wieder an', async () => {
    const t = setup();
    await (await t.tab()).login();
    t.restart();

    const tab = await t.tab();

    expect(tab.account?.username).toBe('erika@example.org');
    await expect(tab.getAccessToken()).resolves.toMatch(/^at-\d+:api:/);
    expect(flows(t.launchWebAuthFlow).map((d) => d.interactive)).toEqual([true, false]);
  });

  it('wirft ohne Sitzung SeedAuthInteractionRequiredError und versucht es je Browser-Sitzung nur einmal still', async () => {
    const t = setup();
    const first = await t.tab();
    await first.login();
    t.entra.refreshTokens.clear();
    t.entra.session = false;
    t.advance(hours(25));

    await expect(first.getAccessToken()).rejects.toBeInstanceOf(SeedAuthInteractionRequiredError);
    const second = await t.tab();
    await expect(second.getAccessToken()).rejects.toBeInstanceOf(SeedAuthInteractionRequiredError);
    expect(t.launchWebAuthFlow).toHaveBeenCalledTimes(2);
    expect(t.session.get(blockedKey)).toEqual({ [config.apiScope]: 'login' });

    // Die Person klickt auf „Anmeldung erneuern“.
    await second.login();
    expect(t.session.has(blockedKey)).toBe(false);
    await expect(first.getAccessToken()).resolves.toMatch(/^at-\d+:api:/);
    expect(flows(t.launchWebAuthFlow).map((d) => d.interactive)).toEqual([true, false, true]);
  });

  it('gibt Netzwerkfehler weiter, ohne still anzumelden', async () => {
    const t = setup();
    const auth = await t.tab();
    await auth.login();
    t.advance(hours(2));
    t.entra.offline = true;

    await expect(auth.getAccessToken()).rejects.toBeInstanceOf(TypeError);

    expect(t.launchWebAuthFlow).toHaveBeenCalledTimes(1);
    expect(t.session.has(blockedKey)).toBe(false);
    t.entra.offline = false;
    await expect(auth.getAccessToken()).resolves.toMatch(/^at-\d+:api:/);
  });

  it('wertet einen Serverfehler des Token-Endpunkts nicht als abgelaufene Sitzung', async () => {
    const t = setup();
    t.local.set(accountKey, { homeAccountId: 'oid-1.tenant-id', username: 'erika@example.org' });
    t.session.set(tokensKey, { refreshToken: 'rt-x', accessTokens: {} });
    const auth = await t.tab({
      fetch: async () => new Response('<html>Service Unavailable</html>', { status: 503 }),
    });

    await expect(auth.getAccessToken()).rejects.toThrow(/HTTP 503/);
    expect(t.launchWebAuthFlow).not.toHaveBeenCalled();
  });

  it('erneuert für parallele Aufrufe in einem Tab und über Tabs hinweg nur einmal', async () => {
    const t = setup();
    const a = await t.tab();
    await a.login();
    const b = await t.tab();
    t.advance(hours(2));

    const tokens = await Promise.all([a.getAccessToken(), a.getAccessToken(), b.getAccessToken(), b.getAccessToken()]);

    expect(new Set(tokens).size).toBe(1);
    expect(grants(t.tokenCalls)).toEqual(['authorization_code', 'refresh_token']);
  });

  it('nimmt einen Aufruf als Callback ohne Scopes als Anfrage für die API', async () => {
    const t = setup();
    const auth = await t.tab();
    await auth.login();

    await expect((auth.getAccessToken as (value: unknown) => Promise<string>)({ queryKey: [] })).resolves.toBe(`at-2:${config.apiScope}`);
    await expect(auth.getAccessToken([])).resolves.toBe(`at-2:${config.apiScope}`);
  });

  it('löscht beim Abmelden Tokens, Konto und Merker in allen Tabs', async () => {
    const t = setup();
    const a = await t.tab();
    await a.login();
    const b = await t.tab();
    await t.session.set(blockedKey, { x: 'login' });

    await b.logout();

    expect(a.account).toBeNull();
    expect(b.account).toBeNull();
    expect([...t.local.keys(), ...t.session.keys()]).toEqual([]);
    await expect(a.getAccessToken()).rejects.toBeInstanceOf(SeedAuthInteractionRequiredError);
    expect(t.launchWebAuthFlow).toHaveBeenCalledTimes(1);
  });

  it('legt die Tokens mit tokenStorage local in chrome.storage.local', async () => {
    const t = setup();
    const auth = await t.tab({ tokenStorage: 'local' });
    await auth.login();
    t.restart();

    const tab = await t.tab({ tokenStorage: 'local' });

    expect(t.local.has(tokensKey)).toBe(true);
    expect(t.session.has(tokensKey)).toBe(false);
    await expect(tab.getAccessToken()).resolves.toBe(`at-2:${config.apiScope}`);
    await tab.logout();
    expect(t.local.size).toBe(0);
  });

  it('gibt den Abbruch der Anmeldung weiter und behält den Stand', async () => {
    const t = setup();
    const auth = await t.tab();
    t.launchWebAuthFlow.mockRejectedValueOnce(new Error('The user did not approve access.'));

    await expect(auth.login()).rejects.toThrow('The user did not approve access.');
    expect(auth.account).toBeNull();
    expect(t.tokenCalls).toHaveLength(0);
  });

  it('nennt den Grund, wenn Entra ID die Anmeldung ablehnt', async () => {
    const t = setup();
    t.entra.consent.clear();
    const auth = await t.tab();

    await expect(auth.login()).rejects.toThrow(/access_denied.*AADSTS65004/);
  });

  it('lehnt eine Antwort mit fremdem state ab', async () => {
    const t = setup();
    t.entra.tamper = (url) => url.replace(/state=[^&]+/, 'state=fremd');
    const auth = await t.tab();

    await expect(auth.login()).rejects.toThrow(/state/);
    expect(t.tokenCalls).toHaveLength(0);
  });

  it('lehnt ein ID-Token mit falscher nonce ab', async () => {
    const t = setup();
    t.entra.nonce = 'andere';
    const auth = await t.tab();

    await expect(auth.login()).rejects.toThrow(/nonce/);
    expect(auth.account).toBeNull();
  });

  it('schreibt nach dem Abmelden keine Tokens aus einer laufenden Erneuerung zurück', async () => {
    const t = setup();
    let release = () => {};
    let reached = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    const atTokenEndpoint = new Promise<void>((resolve) => (reached = resolve));
    const auth = await t.tab({
      fetch: async (input, init) => {
        if (new URLSearchParams(String(init?.body)).get('grant_type') === 'refresh_token') {
          reached();
          await gate;
        }
        return t.fetch(input, init);
      },
    });
    await auth.login();
    t.advance(hours(2));

    // Die Erneuerung wartet am Token-Endpunkt, während die Person sich abmeldet.
    const renewal = auth.getAccessToken();
    await atTokenEndpoint;
    const logout = auth.logout();
    release();
    await Promise.allSettled([renewal, logout]);

    expect(auth.account).toBeNull();
    expect([...t.local.keys(), ...t.session.keys()]).toEqual([]);
  });

  it('wertet eine leere Antwort des unsichtbaren Flusses als nötige Anmeldung', async () => {
    const t = setup();
    const auth = await t.tab();
    await auth.login();
    t.entra.refreshTokens.clear();
    t.advance(hours(25));
    t.launchWebAuthFlow.mockResolvedValueOnce(undefined);

    await expect(auth.getAccessToken()).rejects.toBeInstanceOf(SeedAuthInteractionRequiredError);
  });

  it('meldet fehlende Berechtigungen identity und storage verständlich', async () => {
    vi.stubGlobal('chrome', undefined);
    await expect(createSeedExtensionAuth(config)).rejects.toThrow(/identity und storage/);
  });
});

describe('createSeedExtensionAuth mit Scopes für Graph', () => {
  it('holt Graph-Tokens per Refresh-Token, wenn eingewilligt ist', async () => {
    const t = setup();
    graphScopes.forEach((scope) => t.entra.consent.add(scope));
    const auth = await t.tab();
    await auth.login();

    await expect(auth.getAccessToken(graphScopes)).resolves.toBe(`at-3:${graphScopes.join(',')}`);
    await expect(auth.getAccessToken([...graphScopes].reverse())).resolves.toBe(`at-3:${graphScopes.join(',')}`);
    await expect(auth.getAccessToken()).resolves.toBe(`at-2:${config.apiScope}`);
    expect(grants(t.tokenCalls)).toEqual(['authorization_code', 'refresh_token']);
  });

  it('wirft ohne Einwilligung SeedAuthScopeError, versucht es je Browser-Sitzung einmal still und lässt die API in Ruhe', async () => {
    const t = setup();
    const auth = await t.tab();
    await auth.login();

    await expect(auth.getAccessToken(graphScopes)).rejects.toBeInstanceOf(SeedAuthScopeError);
    const silent = flows(t.launchWebAuthFlow)[1]!;
    expect(silent.interactive).toBe(false);
    expect(new URL(silent.url).searchParams.get('scope')?.split(' ')).toEqual([...graphScopes, 'openid', 'profile', 'offline_access']);

    const tab = await t.tab();
    const error = await tab.getAccessToken(graphScopes).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(SeedAuthScopeError);
    expect((error as SeedAuthScopeError).scopes).toEqual(graphScopes);
    expect(t.launchWebAuthFlow).toHaveBeenCalledTimes(2);

    // Das Refresh-Token bleibt, die API läuft weiter.
    t.advance(hours(2));
    await expect(tab.getAccessToken()).resolves.toMatch(/^at-\d+:api:/);
    expect(grants(t.tokenCalls).at(-1)).toBe('refresh_token');
  });

  it('öffnet außer in login() nie ein Fenster', async () => {
    const t = setup();
    const auth = await t.tab();
    await auth.getAccessToken().catch(() => {});
    await auth.login();
    await auth.getAccessToken(graphScopes).catch(() => {});
    t.entra.refreshTokens.clear();
    t.entra.session = false;
    t.advance(hours(25));
    await auth.getAccessToken().catch(() => {});
    await auth.getAccessToken(graphScopes).catch(() => {});

    expect(flows(t.launchWebAuthFlow).filter((d) => d.interactive)).toHaveLength(1);
  });
});
