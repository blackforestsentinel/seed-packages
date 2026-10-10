import { describe, expect, it, vi } from 'vitest';
import { InteractionRequiredAuthError, type AccountInfo, type IPublicClientApplication } from '@azure/msal-browser';
import { createSeedAuth, isSeedAuthConfig, SeedAuthRedirectError } from './index.js';

const config = { clientId: 'spa-id', tenantId: 'tenant-id', apiScope: 'api://api-id/access_as_user' };
const account = { homeAccountId: 'home', username: 'erika@example.org', name: 'Erika Muster' } as AccountInfo;

function fakeClient(options: { redirectAccount?: AccountInfo; silent?: () => Promise<{ accessToken: string }> } = {}) {
  let active: AccountInfo | null = null;
  const client = {
    handleRedirectPromise: vi.fn(async () => (options.redirectAccount ? { account: options.redirectAccount } : null)),
    getActiveAccount: vi.fn(() => active),
    setActiveAccount: vi.fn((a: AccountInfo | null) => {
      active = a;
    }),
    getAllAccounts: vi.fn(() => []),
    loginRedirect: vi.fn(async () => {}),
    logoutRedirect: vi.fn(async () => {}),
    acquireTokenSilent: vi.fn(options.silent ?? (async () => ({ accessToken: 'token-123' }))),
    acquireTokenRedirect: vi.fn(async () => {}),
  };
  return { client, msal: client as unknown as IPublicClientApplication };
}

describe('createSeedAuth', () => {
  it('übernimmt das Konto aus dem Redirect und liefert Tokens still', async () => {
    const { client, msal } = fakeClient({ redirectAccount: account });

    const auth = await createSeedAuth(config, { client: msal, redirectUri: 'https://app.example.org' });

    expect(auth.account?.username).toBe('erika@example.org');
    await expect(auth.getAccessToken()).resolves.toBe('token-123');
    expect(client.acquireTokenSilent).toHaveBeenCalledWith({ scopes: [config.apiScope], account });
  });

  it('leitet ohne Konto zur Anmeldung um', async () => {
    const { client, msal } = fakeClient();

    const auth = await createSeedAuth(config, { client: msal, redirectUri: 'https://app.example.org' });

    expect(auth.account).toBeNull();
    await expect(auth.getAccessToken()).rejects.toBeInstanceOf(SeedAuthRedirectError);
    expect(client.loginRedirect).toHaveBeenCalledWith({ scopes: [config.apiScope] });
  });

  it('leitet um, wenn MSAL eine Interaktion verlangt', async () => {
    const { client, msal } = fakeClient({
      redirectAccount: account,
      silent: async () => {
        throw new InteractionRequiredAuthError('interaction_required');
      },
    });

    const auth = await createSeedAuth(config, { client: msal, redirectUri: 'https://app.example.org' });

    await expect(auth.getAccessToken()).rejects.toBeInstanceOf(SeedAuthRedirectError);
    expect(client.acquireTokenRedirect).toHaveBeenCalledWith({ scopes: [config.apiScope], account });
  });
});

describe('isSeedAuthConfig', () => {
  it('erkennt vollständige und unvollständige Konfigurationen', () => {
    expect(isSeedAuthConfig(config)).toBe(true);
    expect(isSeedAuthConfig({ clientId: 'spa-id' })).toBe(false);
    expect(isSeedAuthConfig(undefined)).toBe(false);
  });
});
