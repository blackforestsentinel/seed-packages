# @blackforestsentinel/seed-web-people

React-Komponenten rund um Personen für Frontends aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template): Profilbild, Name, Rolle, Abmelden, Personenauswahl, Karten, Anmelde- und Kein-Zugriff-Seite. Die Daten kommen aus Microsoft Graph, mit dem Token der angemeldeten Person direkt im Browser; einen eigenen Server-Endpunkt braucht es nicht.

- Optik an Fluent 2 angelehnt (Teams, Outlook), ohne Abhängigkeit von `@fluentui/*`.
- Ein statisches Stylesheet, keine eingefügten Styles; passt zur CSP der Seed-Apps (`style-src 'self'`).
- Bilder als `data:`-URL; die CSP erlaubt `img-src 'self' data:` schon.
- Rund 16 KB JavaScript und 6 KB CSS nach gzip, ohne React. Einzelne Komponenten lassen sich per Tree-Shaking einbinden.

## Einbau

```bash
npm install @blackforestsentinel/seed-web-people
```

Die Version passt zu `@blackforestsentinel/seed-web-auth` (Peer-Abhängigkeit, ebenso React ab 18).

```tsx
// main.tsx
import '@blackforestsentinel/seed-web-people/styles.css';
import { SeedUserProvider } from '@blackforestsentinel/seed-web-auth/react';
import { DirectoryProvider, SignInPage } from '@blackforestsentinel/seed-web-people';

const config = await loadRuntimeConfig<AppConfig>();
const auth = await startAuth(config.auth); // createSeedAuth oder null (lokal, ohne sso)

if (auth && !auth.account) {
  root.render(
    <DirectoryProvider config={config.auth} auth={auth}>
      <SignInPage appName="Kundenportal" />
    </DirectoryProvider>,
  );
} else {
  root.render(
    <SeedUserProvider load={api.getMe}>
      <DirectoryProvider config={config.auth} auth={auth}>
        <App />
      </DirectoryProvider>
    </SeedUserProvider>,
  );
}
```

- `DirectoryProvider` entscheidet anhand von `config.auth`: mit `auth.directory` und Anmeldung Graph, im lokalen Modus die Liste aus `config.json`, sonst gar nichts. Ohne `auth.directory` zeigen die Komponenten Namen und Initialen, die die App übergibt, und rufen Graph nie auf.
- `DirectoryProvider` gehört in `SeedUserProvider`, damit Rollen und Capabilities aus `/api/me` ankommen.
- Die CSP der App braucht `connect-src https://graph.microsoft.com`; das Template trägt das mit `auth.directory` ein.

## Komponenten

```tsx
<CurrentUserMenu menuItems={[{ label: 'Hilfe', href: '/hilfe' }]} />
<PersonLabel id={invoice.approvedBy} secondary={formatDate(invoice.approvedAt)} card />
<PeoplePicker multiple value={reviewers} onChange={setReviewers} onlyWithAppRole roles={['Admin']} />
<PeoplePicker multiple value={writers} onChange={setWriters} principalTypes={['user', 'servicePrincipal']} />
<AvatarGroup people={task.participants} max={4} />
```

| Komponente | Zeigt | Wichtige Props |
| --- | --- | --- |
| `Avatar` | Foto; ohne Foto Initialen auf einer Farbe aus der ID; Platzhalter beim Laden; für Anwendungen ein Symbol | `id`, `name`, `image`, `kind`, `servicePrincipalType`, `size` (16 bis 96, Default 32), `shape` (`circular`, `square`), `presence` (`true` oder fester Status), `color`, `decorative` |
| `PersonLabel` | Avatar, Name, optional zweite Zeile; mit `card` die `PersonCard` beim Überfahren und per Tastatur | `id`, `name`, `person`, `secondary`, `secondaryField` (`mail`, `jobTitle`, `department`), `size`, `presence`, `card` |
| `PersonCard` | größeres Bild, Name, Position, Abteilung, Status, Mail, Link zum Teams-Chat | `id`, `person`, `name`, `presence`, `chat`, `actions` |
| `AvatarGroup` | mehrere Avatare überlappend, ab `max` „+n“ mit den übrigen Namen als Titel | `people` (IDs oder `{ id, name }`), `max` (Default 4), `size` |
| `CurrentUserMenu` | Avatar, Name, Rolle (Anzeigename aus `/api/me`), Abmelden; mit `menuItems` ein Menü | `menuItems`, `renderTrigger`, `showRole`, `presence`, `onSignOut` (`null` blendet aus) |
| `PeoplePicker` | Suchfeld mit Vorschlägen, eine oder mehrere Personen als Chips | `value`/`defaultValue`, `onChange`, `multiple`, `filter`, `renderOption`, `onlyWithAppRole`, `roles`, `principalTypes`, `servicePrincipalTypes`, `minChars` (2), `debounceMs` (250), `maxSuggestions` (8), `labels` |
| `SignInPage` | Anmeldeseite mit App-Name und Knopf | `appName`, `description`, `logo`, `onSignIn` (Default `auth.login()`), `error`, `footer` |
| `AccessDenied` | angemeldet, aber ohne Rolle oder Capability: Erklärung, Ansprechperson, Kontowechsel | `appName`, `reason` (`no-role`, `missing-capability`), `capability`, `contact`, `actions`, `onSignOut` |
| `RoleBadge` | Rolle als Etikett, Anzeigename aus `/api/me` | `role`, `displayName`, `appearance` (`tint`, `filled`, `outline`) |
| `PresenceBadge` | Teams-Status als Punkt | `status`, `size`, `label` |

### PeoplePicker

- ARIA-Muster „Combobox“: Pfeiltasten wählen den Vorschlag, Enter übernimmt ihn, Escape schließt die Liste und leert dann das Feld, Rücktaste im leeren Feld entfernt den letzten Chip.
- Suche ab zwei Zeichen mit 250 ms Verzögerung. Zuerst die für die Person relevanten Kontakte (`/me/people`), dann das Verzeichnis (`/users?$search`), beides in einem `$batch`.
- `filter` blendet Vorschläge aus, `renderOption` stellt sie selbst dar.
- **`onlyWithAppRole`** zeigt nur Personen mit einer App-Rolle der API, mit `roles` nur mit diesen Rollen. Das Paket liest die Zuweisungen der API-Registrierung (`/servicePrincipals(appId='…')/appRoleAssignedTo`) und löst Gruppen samt verschachtelter Gruppen auf (`/groups/{id}/transitiveMembers`). Das Ergebnis bleibt zehn Minuten im Zwischenspeicher.
- Ohne `auth.directory.roleFilter` zeigt der Picker mit `onlyWithAppRole` niemanden, sondern einen Fehler im Feld (`data-state="error"`) und in der Konsole. Im lokalen Modus filtert er nach `roles` der erfundenen Personen.

### Anwendungen und verwaltete Identitäten

Wer einen Datensatz bearbeiten darf, ist oft nicht nur eine Person, sondern auch eine verwaltete Identität (Managed Identity) oder eine andere Anwendung. Mit `auth.directory.servicePrincipals` (in `project.yaml` `auth.directory.servicePrincipals: true`) kennen die Komponenten auch diese Service Principals:

```tsx
<PeoplePicker
  multiple
  principalTypes={['user', 'servicePrincipal']}
  servicePrincipalTypes={['ManagedIdentity', 'Application']}
  value={writers}
  onChange={setWriters}
/>
<PersonLabel id={record.updatedBy} secondaryField="jobTitle" card />
```

- `Person.kind` ist `user` oder `servicePrincipal`. Für Service Principals kommen `appId` (Client-ID) und `servicePrincipalType` (`ManagedIdentity`, `Application`) dazu. Gespeicherte Daten ohne `kind` gelten als Person.
- **`principalTypes`** im Picker, Default `['user']`. Mit `servicePrincipal` sucht er zusätzlich über `/servicePrincipals?$search="displayName:…"`, im selben `$batch` wie die Personen. **`servicePrincipalTypes`** beschränkt die Suche, etwa auf `['ManagedIdentity']`. Ohne Beschränkung erscheinen auch Anwendungen von Microsoft (etwa „Microsoft Graph“); `filter` blendet weitere aus.
- Personen und Anwendungen stehen abwechselnd in den Vorschlägen. Anwendungen zeigen ihre Art und die App-ID als zweite Zeile.
- Mit `onlyWithAppRole` erscheinen Service Principals, denen eine Rolle der API **direkt** zugewiesen ist. Gruppen löst der Filter nur nach Personen auf.
- **`PersonLabel`, `PersonCard`, `Avatar`** erkennen Service Principals an der Objekt-ID: Gibt es keine Person dazu (`/users/{id}`), fragen sie `/servicePrincipals/{id}`. Statt Initialen zeigen sie ein Symbol auf einem Quadrat, einen Schlüssel für verwaltete Identitäten, sonst vier Kacheln. Foto und Teams-Status gibt es für Anwendungen nicht, Mail und Chat auf der Karte auch nicht; die Karte nennt Art und App-ID.
- Mit dem Schalter lädt jedes `PersonLabel` und jeder `Avatar` mit `id` das Profil, um die Art zu kennen, auch wenn `name` gesetzt ist (im selben `$batch`, 30 Minuten im Zwischenspeicher). Den Teams-Status lädt das Label erst, wenn feststeht, dass es eine Person ist. Wer die Art schon kennt, gibt sie mit und spart die Abfrage: `person={{ kind: 'user' }}` bzw. `kind` am `Avatar`.
- `secondaryField` zeigt bei Anwendungen deren Art („Verwaltete Identität“, „Anwendung“), wenn das Feld leer ist.
- Ohne `auth.directory.servicePrincipals` meldet der Picker mit `principalTypes` einen Fehler im Feld und in der Konsole, statt nur Personen zu zeigen. Labels und Karten bleiben wie bisher: Für die ID einer Anwendung zeigen sie den übergebenen Namen mit Initialen.
- Im lokalen Modus stehen Anwendungen mit `"kind": "servicePrincipal"` in `auth.directory.people`.

## Hooks

Für eigene Oberflächen gibt es nur die Daten:

| Hook | Liefert |
| --- | --- |
| `useCurrentUser()` | `{ status, user }`; `user` mit `id`, `displayName`, `username`, `roles`, `roleNames`, `capabilities`. Der Name kommt sofort aus der Anmeldung, Rollen aus `/api/me`. |
| `usePerson(id)` | `{ status, person }` mit Name, Mail, Position, Abteilung |
| `usePhoto(id, size)` | `{ status, url }` als `data:`-URL oder `null` |
| `usePeopleSearch(text, options)` | `{ status, people, query }`, mit Verzögerung und Mindestlänge; `principalTypes` und `servicePrincipalTypes` wie im Picker |
| `usePresence(id)` | `{ status, presence }`, gebündelt und jede Minute aktualisiert, solange sichtbar |
| `useAppRoleMembers(roles, options)` | `{ status, members, ids }`, Personen mit App-Rolle; mit `principalTypes` auch Service Principals |
| `useDirectory()`, `useSignOut()` | das Verzeichnis; Abmelden samt Leeren der Zwischenspeicher |

`status` ist `idle` (nichts zu laden), `loading`, `ready` oder `error`. Ohne React: `createGraphDirectory()` und `createLocalDirectory()`.

## Anpassen

- **CSS-Variablen** für alles Sichtbare, an einer Stelle gesetzt:
  ```css
  :root {
    --seed-accent: #8a2be2;
    --seed-font: Inter, sans-serif;
    --seed-avatar-radius: 8px;
  }
  ```
  Die wichtigsten: `--seed-font`, `--seed-accent` (mit `-hover`, `-pressed`), `--seed-text` (mit `-secondary`, `-tertiary`), `--seed-surface`, `--seed-stroke`, `--seed-focus`, `--seed-radius-*`, `--seed-shadow-*`, `--seed-presence-*`, `--seed-palette-<farbe>-bg`/`-fg`. Die Werte entsprechen den Tokens von Fluent 2 (Brand `#0F6CBD`).
- **`@layer seed`:** Alle Standard-Styles liegen in dieser Ebene. Jede Regel der App gewinnt ohne `!important`, auch eine mit geringerer Spezifität.
- **Feste Klassennamen** nach BEM, etwa `seed-avatar`, `seed-avatar__image`, `seed-picker__option`, `seed-user-menu__role`. Zustände als `data-*`: `data-state="loading"`, `data-active`, `data-open`, `data-presence="busy"`, `data-color="cranberry"`.
- **`className` und `classNames`:** Jede Komponente nimmt `className` für das Wurzelelement und `classNames` für ihre Teile, z. B. `classNames={{ option: 'px-2 py-1' }}`; das passt auch zu Tailwind.
- **Eigene Darstellung:** `renderOption` im Picker, `renderTrigger` und `menuItems` im Benutzermenü, die Hooks für alles andere.
- **Hell und Dunkel** folgen `prefers-color-scheme`. `data-seed-theme="light"` oder `"dark"` am `html`-Element legt es fest, etwa für einen eigenen Umschalter.
- **Bedienbarkeit:** Picker als Combobox, Benutzermenü als Menu Button, Bilder mit Alternativtext über `role="img"` und `aria-label`, alles mit der Tastatur bedienbar, Fokusrahmen wie Fluent, `forced-colors` berücksichtigt.

## Berechtigungen und Einwilligung

Alle Berechtigungen sind delegiert: Das Frontend sieht nur, was die angemeldete Person in Outlook oder Teams ohnehin sieht. Das Terraform-Modul `sso` trägt sie an der SPA-Registrierung ein; einwilligen muss ein Mensch.

| Berechtigung | Wofür | Admin-Consent nötig |
| --- | --- | --- |
| `User.Read` | eigenes Profil | nein |
| `User.ReadBasic.All` | Profile und Fotos anderer, Verzeichnissuche | nein |
| `People.Read` | relevante Personen im Picker | nein |
| `Presence.Read.All` | Teams-Status (`presence: true`) | nein |
| `Application.Read.All` | Rollenzuweisungen der API (`roleFilter: true`), Service Principals suchen und anzeigen (`servicePrincipals: true`) | **ja** |
| `GroupMember.Read.All` | Mitglieder zugewiesener Gruppen (`roleFilter: true`) | **ja** |

- Viele Tenants erlauben keine eigene Einwilligung der Personen. Dann erteilt ein Admin sie einmal für alle: Entra ID, App-Registrierungen, die SPA-Registrierung, „API-Berechtigungen“, „Administratorzustimmung erteilen“.
- Das Paket holt ein Token für alle Scopes aus `auth.directory.scopes` zugleich. Fehlt die Einwilligung für einen davon, bleibt Graph ganz aus. Mit `roleFilter: true` oder `servicePrincipals: true` daher erst die Administratorzustimmung erteilen, dann ausrollen.
- Fehlt die Einwilligung, leitet seed-web-auth je Tab einmal ohne und einmal mit Dialog zu Entra ID um, damit die Person zustimmen kann. Danach läuft die App ohne Graph weiter: Initialen statt Fotos, der Picker meldet einen Fehler, in der Konsole steht ein Hinweis.
- Lehnt Graph Position und Abteilung ab (403), fragt das Paket nur Name und Mail ab.

## Lokaler Modus

Mit `"auth": { "mode": "local" }` gibt es kein MSAL und keinen Graph-Zugriff. Unter `auth.directory.people` stehen erfundene Personen; Avatare zeigen Initialen, der Picker sucht in dieser Liste, einen Status gibt es nicht.

```json
{
  "apiBaseUrl": "http://localhost:7071",
  "auth": {
    "mode": "local",
    "directory": {
      "people": [
        { "id": "00000000-0000-4000-8000-000000000001", "displayName": "Erika Mustermann", "mail": "erika.mustermann@example.org", "jobTitle": "Buchhalterin", "department": "Buchhaltung", "roles": ["Admin"] },
        { "id": "00000000-0000-4000-8000-000000000101", "displayName": "Nachtlauf", "kind": "servicePrincipal", "servicePrincipalType": "ManagedIdentity", "appId": "00000000-0000-4000-8000-000000000201", "roles": ["Admin"] }
      ]
    }
  }
}
```

`roles` ist optional und nur für `onlyWithAppRole` im lokalen Modus. Einträge mit `"kind": "servicePrincipal"` findet der Picker nur mit `principalTypes`. Ohne `auth.directory` sucht der Picker nicht und sagt das.

## Laufzeit und Datenschutz

- Bilder lädt das Paket nur, wenn eine Komponente sie zeigt, in festen Größen (48, 64 oder 96 Pixel aus `/photos/{size}/$value`).
- Anfragen innerhalb weniger Millisekunden gehen gemeinsam als `$batch` (bis 20 je Aufruf). Gleiche laufende Anfragen werden zusammengefasst, gedrosselte (429) nach `Retry-After` wiederholt.
- Der Teams-Status kommt gebündelt über `/communications/getPresencesByUserId` und wird jede Minute neu geladen, solange er sichtbar ist und der Tab im Vordergrund liegt.
- Zwischenspeicher im Speicher und im `sessionStorage` (endet mit dem Tab), getrennt je Konto: Profile und Fotos 30 Minuten, Rollenzuweisungen 10 Minuten, Suchergebnisse und Status nur im Speicher. Personen ohne Foto merkt er sich auch. Abmelden über die Komponenten leert alles.
- Fotos und Profile gehen nie an die eigene API und nicht in Logs. Profilbilder sind personenbezogene Daten; sie sind aber für dieselben Personen sichtbar wie in Teams und Outlook, und die Einstellungen des Tenants (etwa ausgeblendete Fotos) gelten weiter.
