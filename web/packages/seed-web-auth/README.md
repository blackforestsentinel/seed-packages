# @blackforestsentinel/seed-web-auth

Anmeldung per MSAL für Frontends in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template). Die Werte kommen aus der Laufzeitkonfiguration, die das Terraform-Modul `sso` über die Pipeline in `config.json` schreibt.

```ts
import { createHttpClient, loadRuntimeConfig } from '@blackforestsentinel/seed-web-core';
import { createSeedAuth, isSeedAuthConfig } from '@blackforestsentinel/seed-web-auth';

const config = await loadRuntimeConfig<{ apiBaseUrl: string; auth?: unknown }>();
if (isSeedAuthConfig(config.auth)) {
  const auth = await createSeedAuth(config.auth);
  if (!auth.account) await auth.login();
  const api = createHttpClient({ baseUrl: config.apiBaseUrl, getAccessToken: auth.getAccessToken });
}
```

- `createSeedAuth()` schließt eine laufende Anmeldung per Redirect ab; danach ist `account` gesetzt.
- `getAccessToken()` holt Tokens still und leitet nur um, wenn Entra ID eine Interaktion verlangt. Dann wirft es `SeedAuthRedirectError`.
- Redirect-Ziel ist der Origin der Seite mit abschließendem Schrägstrich (`https://app.example.org/`). Das Terraform-Modul `sso` trägt genau diese Form für die Static Web App und in `dev` für `http://localhost:5173/` ein.
