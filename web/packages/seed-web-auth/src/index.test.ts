import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  BrowserAuthError,
  BrowserAuthErrorCodes,
  CacheLookupPolicy,
  InteractionRequiredAuthError,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser';
import { createSeedAuth, isSeedAuthConfig, isSeedLocalAuthConfig, SeedAuthRedirectError } from './index.js';

const config = { clientId: 'spa-id', tenantId: 'tenant-id', apiScope: 'api://api-id/access_as_user' };
const bridged = { ...config, redirectBridgePath: '/redirect.html' };
const options = { redirectUri: 'https://app.example.org/', redirectBridgeUri: 'https://app.example.org/redirect.html' };
const account = { homeAccountId: 'home', username: 'erika@example.org', name: 'Erika Muster' } as AccountInfo;
const hintKey = 'seed-auth.spa-id.login-hint';
const attemptKey = 'seed-auth.spa-id.silent-redirect';

interface FakeOptions {
  redirect?: () => Promise<{ account: AccountInfo } | null>;
  cached?: AccountInfo;
  silent?: () => Promise<{ accessToken: string; account?: AccountInfo }>;
  ssoSilent?: () => Promise<{ accessToken: string; account: AccountInfo }>;
}

function fakeClient(options: FakeOptions = {}) {
  let active: AccountInfo | null = null;
  const client = {
    handleRedirectPromise: vi.fn(options.redirect ?? (async () => null)),
    getActiveAccount: vi.fn(() => active),
    setActiveAccount: vi.fn((a: AccountInfo | null) => {
      active = a;
    }),
    getAllAccounts: vi.fn(() => (options.cached ? [options.cached] : [])),
    loginRedirect: vi.fn(async () => {}),
    logoutRedirect: vi.fn(async () => {}),
    acquireTokenSilent: vi.fn(options.silent ?? (async () => ({ accessToken: 'token-123' }))),
    acquireTokenRedirect: vi.fn(async () => {}),
    ssoSilent: vi.fn(
      options.ssoSilent ??
        (async () => {
          throw new InteractionRequiredAuthError('login_required');
        }),
    ),
  };
  return { client, msal: client as unknown as IPublicClientApplication };
}

function memoryStorage(): Storage {
  const values = new Map<string, string>();
  return {
    get length() {
      return values.size;
    },
    clear: () => values.clear(),
    getItem: (key) => values.get(key) ?? null,
    key: (index) => [...values.keys()][index] ?? null,
    removeItem: (key) => void values.delete(key),
    setItem: (key, value) => void values.set(key, value),
  };
}

const expired = async (): Promise<never> => {
  throw new InteractionRequiredAuthError('invalid_grant');
};

beforeEach(() => {
  vi.stubGlobal('localStorage', memoryStorage());
  vi.stubGlobal('sessionStorage', memoryStorage());
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('createSeedAuth', () => {
  it('übernimmt das Konto aus dem Redirect und liefert Tokens ohne iframe', async () => {
    const { client, msal } = fakeClient({ redirect: async () => ({ account }) });

    const auth = await createSeedAuth(config, { client: msal, redirectUri: options.redirectUri });

    expect(auth.account?.username).toBe('erika@example.org');
    await expect(auth.getAccessToken()).resolves.toBe('token-123');
    expect(client.acquireTokenSilent).toHaveBeenCalledWith({
      scopes: [config.apiScope],
      account,
      cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
    });
    expect(localStorage.getItem(hintKey)).toBe('erika@example.org');
  });

  it('leitet beim ersten Besuch interaktiv zur Anmeldung um', async () => {
    const { client, msal } = fakeClient();

    const auth = await createSeedAuth(bridged, { client: msal, ...options });

    expect(auth.account).toBeNull();
    expect(client.ssoSilent).not.toHaveBeenCalled();
    await expect(auth.getAccessToken()).rejects.toBeInstanceOf(SeedAuthRedirectError);
    expect(client.loginRedirect).toHaveBeenCalledWith({ scopes: [config.apiScope] });
  });

  it('meldet bekannte Personen in einem neuen Tab still per ssoSilent an', async () => {
    localStorage.setItem(hintKey, 'erika@example.org');
    const { client, msal } = fakeClient({ ssoSilent: async () => ({ accessToken: 'sso-token', account }) });

    const auth = await createSeedAuth(bridged, { client: msal, ...options });

    expect(client.ssoSilent).toHaveBeenCalledWith({
      scopes: [config.apiScope],
      loginHint: 'erika@example.org',
      redirectUri: options.redirectBridgeUri,
    });
    expect(auth.account).toBe(account);
  });

  it('versucht ohne Bridge-Seite kein ssoSilent, sondern leitet ohne Dialog um', async () => {
    localStorage.setItem(hintKey, 'erika@example.org');
    const { client, msal } = fakeClient();

    const auth = await createSeedAuth(config, { client: msal, redirectUri: options.redirectUri });
    await auth.login();

    expect(client.ssoSilent).not.toHaveBeenCalled();
    expect(client.loginRedirect).toHaveBeenCalledWith({
      scopes: [config.apiScope],
      loginHint: 'erika@example.org',
      prompt: 'none',
    });
  });

  it('versucht nach abgelaufenem Refresh-Token erst ssoSilent', async () => {
    const { client, msal } = fakeClient({
      cached: account,
      silent: expired,
      ssoSilent: async () => ({ accessToken: 'sso-token', account }),
    });

    const auth = await createSeedAuth(bridged, { client: msal, ...options });

    await expect(auth.getAccessToken()).resolves.toBe('sso-token');
    expect(client.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('leitet bei redirect_bridge_timeout erst ohne Dialog, dann interaktiv um', async () => {
    const timeout = async (): Promise<never> => {
      throw new BrowserAuthError(BrowserAuthErrorCodes.timedOut, 'redirect_bridge_timeout');
    };
    const { client, msal } = fakeClient({ cached: account, silent: timeout });

    const first = await createSeedAuth(bridged, { client: msal, ...options });
    await expect(first.getAccessToken()).rejects.toBeInstanceOf(SeedAuthRedirectError);

    // Die Umleitung mit prompt=none kam ohne Sitzung zurück; die Seite startet neu.
    client.handleRedirectPromise.mockRejectedValueOnce(new InteractionRequiredAuthError('login_required'));
    const second = await createSeedAuth(bridged, { client: msal, ...options });
    await expect(second.getAccessToken()).rejects.toBeInstanceOf(SeedAuthRedirectError);

    expect(client.ssoSilent).toHaveBeenCalledTimes(2);
    expect(client.acquireTokenRedirect).toHaveBeenNthCalledWith(1, {
      scopes: [config.apiScope],
      loginHint: 'erika@example.org',
      prompt: 'none',
      account,
    });
    expect(client.acquireTokenRedirect).toHaveBeenNthCalledWith(2, {
      scopes: [config.apiScope],
      loginHint: 'erika@example.org',
      account,
    });
  });

  it('leitet bei Netzwerkfehlern nicht um', async () => {
    const offline = async (): Promise<never> => {
      throw new BrowserAuthError(BrowserAuthErrorCodes.noNetworkConnectivity);
    };
    const { client, msal } = fakeClient({ cached: account, silent: offline });

    const auth = await createSeedAuth(bridged, { client: msal, ...options });

    await expect(auth.getAccessToken()).rejects.toBeInstanceOf(BrowserAuthError);
    expect(client.ssoSilent).not.toHaveBeenCalled();
    expect(client.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('startet für parallele Aufrufe nur eine Erneuerung', async () => {
    const { client, msal } = fakeClient({ cached: account, silent: expired });

    const auth = await createSeedAuth(bridged, { client: msal, ...options });
    const results = await Promise.allSettled([auth.getAccessToken(), auth.getAccessToken(), auth.getAccessToken()]);

    expect(results.every((r) => r.status === 'rejected' && r.reason instanceof SeedAuthRedirectError)).toBe(true);
    expect(client.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('löscht den Hinweis auf das Konto beim Abmelden', async () => {
    const { client, msal } = fakeClient({ cached: account });

    const auth = await createSeedAuth(config, { client: msal, redirectUri: options.redirectUri });
    await auth.logout();

    expect(localStorage.getItem(hintKey)).toBeNull();
    expect(client.logoutRedirect).toHaveBeenCalledWith({ account });
  });

  it('gibt andere Fehler aus dem Redirect weiter', async () => {
    const { msal } = fakeClient({
      redirect: async () => {
        throw new BrowserAuthError(BrowserAuthErrorCodes.noStateInHash);
      },
    });

    await expect(createSeedAuth(config, { client: msal, redirectUri: options.redirectUri })).rejects.toBeInstanceOf(BrowserAuthError);
    expect(sessionStorage.getItem(attemptKey)).toBeNull();
  });
});

describe('isSeedAuthConfig', () => {
  it('erkennt vollständige und unvollständige Konfigurationen', () => {
    expect(isSeedAuthConfig(config)).toBe(true);
    expect(isSeedAuthConfig({ clientId: 'spa-id' })).toBe(false);
    expect(isSeedAuthConfig(undefined)).toBe(false);
  });

  it('erkennt den lokalen Modus', () => {
    expect(isSeedLocalAuthConfig({ mode: 'local' })).toBe(true);
    expect(isSeedLocalAuthConfig(config)).toBe(false);
    expect(isSeedAuthConfig({ mode: 'local' })).toBe(false);
  });
});
