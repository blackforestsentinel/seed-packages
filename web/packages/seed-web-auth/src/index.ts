import {
  createStandardPublicClientApplication,
  InteractionRequiredAuthError,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser';

/** Auth-Teil der Laufzeitkonfiguration (config.json), kommt aus dem Terraform-Modul sso. */
export interface SeedAuthConfig {
  /** Client-ID der SPA-App-Registrierung. */
  clientId: string;
  tenantId: string;
  /** Delegierte Berechtigung der API, z. B. api://<client-id>/access_as_user. */
  apiScope: string;
}

export interface SeedAuthOptions {
  /**
   * Ziel nach Login und Logout. Default ist der Origin der Seite mit abschließendem
   * Schrägstrich, so wie das Terraform-Modul sso ihn an der App-Registrierung hinterlegt.
   */
  redirectUri?: string;
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

/**
 * Richtet MSAL ein und schließt eine laufende Anmeldung per Redirect ab. Danach ist
 * account gesetzt, wenn die Person angemeldet ist.
 */
export async function createSeedAuth(config: SeedAuthConfig, options: SeedAuthOptions = {}): Promise<SeedAuth> {
  const redirectUri = options.redirectUri ?? `${window.location.origin}/`;
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

  const result = await client.handleRedirectPromise();
  const account = result?.account ?? client.getActiveAccount() ?? client.getAllAccounts()[0] ?? null;
  if (account) {
    client.setActiveAccount(account);
  }

  return {
    get account() {
      return client.getActiveAccount();
    },
    login: () => client.loginRedirect({ scopes }),
    logout: () => client.logoutRedirect({ account: client.getActiveAccount() ?? undefined }),
    async getAccessToken() {
      const active = client.getActiveAccount();
      if (!active) {
        await client.loginRedirect({ scopes });
        throw new SeedAuthRedirectError();
      }

      try {
        return (await client.acquireTokenSilent({ scopes, account: active })).accessToken;
      } catch (error) {
        if (error instanceof InteractionRequiredAuthError) {
          await client.acquireTokenRedirect({ scopes, account: active });
          throw new SeedAuthRedirectError();
        }
        throw error;
      }
    },
  };
}
