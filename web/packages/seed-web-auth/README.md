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

## Stille Sitzungserneuerung

Entra ID begrenzt das Refresh-Token einer SPA auf 24 Stunden. Danach erneuert `seed-web-auth` die Sitzung in dieser Reihenfolge und geht erst zum nächsten Schritt, wenn der vorige scheitert:

1. Access-Token aus dem Cache oder per Refresh-Token, ohne iframe.
2. `ssoSilent` mit dem Benutzernamen als `loginHint` in einem unsichtbaren iframe. Das nutzt die Sitzung bei Entra ID statt des abgelaufenen Refresh-Tokens, braucht aber die Bridge-Seite (unten) und Cookies von Drittanbietern.
3. Umleitung zu Entra ID mit `loginHint` und `prompt=none`: Mit Sitzung kommt die Seite sofort zurück, ohne Sitzung mit einem Fehler statt eines Anmeldedialogs. Erst der nächste Versuch im selben Tab ist interaktiv.

Netzwerkfehler leiten nicht um, alle anderen Fehler beim stillen Erneuern schon; auch `timed_out` und `redirect_bridge_timeout`, die MSAL statt eines `InteractionRequiredAuthError` meldet. Parallele Aufrufe teilen sich eine Erneuerung. Der Benutzername liegt im `localStorage`, damit auch ein neuer Tab still anmelden kann; `logout()` löscht ihn. Wer noch nie angemeldet war, wird direkt interaktiv umgeleitet.

### Bridge-Seite

MSAL 5 lädt die Redirect-URI der stillen Anmeldung im iframe und erwartet dort eine eigene Seite, die die Antwort per `broadcastResponseToMainFrame` an die App weiterreicht. Die Startseite der App kann das nicht. Ohne Bridge-Seite überspringt `seed-web-auth` den Schritt `ssoSilent` und leitet direkt um.

```html
<!-- frontend/redirect.html, in vite.config.ts als zweiter Einstieg -->
<script type="module" src="/src/redirect.ts"></script>
```

```ts
// frontend/src/redirect.ts
import { handleSeedAuthRedirect } from '@blackforestsentinel/seed-web-auth/bridge';
void handleSeedAuthRedirect();
```

Die Redirect-URI `<origin>/redirect.html` trägt das Modul `sso` mit `spa_redirect_bridge_path = "/redirect.html"` ein und schreibt `redirectBridgePath` in `config.json`.

## Capabilities im UI

Die API liefert unter `GET /api/me` die angemeldete Person samt Capabilities, aufgelöst aus ihren App-Rollen (`SeedUserInfo` aus `Bfs.Seed.Auth`). Das Frontend kennt die Zuordnung nicht, es blendet nur ein oder aus. Durchgesetzt werden die Rechte in der API per `[RequireCapability]`.

```tsx
import { IfCapability, SeedUserProvider, loadSeedUser, useCapabilities } from '@blackforestsentinel/seed-web-auth/react';

<SeedUserProvider load={() => loadSeedUser(http)}>
  <IfCapability capability="invoices.write" fallback={<p>Nur lesen</p>}>
    <button>Rechnung anlegen</button>
  </IfCapability>
</SeedUserProvider>

const canWrite = useCapabilities().has('invoices.write');
```

- `SeedUserProvider` lädt die Person einmal beim Einhängen; solange sie lädt, ohne Anmeldung oder bei einem Fehler sind keine Capabilities gesetzt und geschützte Elemente bleiben aus.
- `useSeedUser()` liefert `{ status, user }` mit `status` `loading`, `ready` oder `error`.
- Ohne React: `loadSeedUser(http)` und `hasCapability(user, 'invoices.write')` aus dem Haupteinstieg.

Der Einstieg `/react` braucht React ab Version 18 und lädt MSAL nicht.

## Lokaler Modus

Steht in `config.json` `"auth": { "mode": "local" }`, startet das Frontend ohne MSAL (`isSeedLocalAuthConfig()`); die API läuft dann mit `Auth:Mode = Local` und liefert unter `/api/me` den Entwicklungsnutzer mit seinen Capabilities.
