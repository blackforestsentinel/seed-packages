# Sentinel Seed – Code-Pakete

Code-Bausteine für Projekte aus dem [Sentinel-Seed-Template](https://github.com/blackforestsentinel/seed-template). Projekte ziehen sie als Dependency mit fester Version, statt Code zu kopieren.

| Paket | Registry | Inhalt |
| --- | --- | --- |
| [`Bfs.Seed.Functions.Core`](dotnet/src/Bfs.Seed.Functions.Core) | nuget.org | `AddSeedCore()`: Application Insights, `SeedOptions`, `SeedHealthReport`, `SeedSecrets` mit Prüfung beim Start |
| [`@blackforestsentinel/seed-web-core`](web/packages/seed-web-core) | npm | `loadRuntimeConfig()`, `createHttpClient()` |
| [`Bfs.Seed.Auth`](dotnet/src/Bfs.Seed.Auth) | nuget.org | `UseSeedAuth()`: Token-Prüfung gegen Entra ID, `[AllowAnonymous]` für Ausnahmen |
| [`@blackforestsentinel/seed-web-auth`](web/packages/seed-web-auth) | npm | `createSeedAuth()`: Login per MSAL, Access-Tokens für die API |

Geplant: `Bfs.Seed.Storage`, `Bfs.Seed.OpenApi`.

## Entwickeln

```bash
cd dotnet && dotnet test --solution Bfs.Seed.slnx
cd web && npm install && npm run build && npm test
```

## Veröffentlichen

Alle Pakete erscheinen gemeinsam in der Version des Git-Tags. Veröffentlicht wird nur aus [`release.yml`](.github/workflows/release.yml), nie lokal:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Der Workflow authentifiziert sich per Trusted Publishing (OIDC) bei nuget.org und npm. Einrichtung:

- **nuget.org:** Trusted-Publishing-Richtlinie für `blackforestsentinel/seed-packages`, Workflow `release.yml`, Environment `release`. Variable `NUGET_USER` im Environment `release` mit dem nuget.org-Benutzernamen.
- **npm:** Trusted Publisher je Paket auf npmjs.com (`seed-packages`, `release.yml`, Environment `release`, „Allow npm publish“). Eine neue Verbindung verfällt nach 48 Stunden, wenn bis dahin keine Veröffentlichung über sie läuft.
- **Neues npm-Paket:** Trusted Publishing lässt sich erst einrichten, wenn das Paket existiert. Der Workflow erkennt neue Pakete und stellt sie per `npm stage publish` bereit; dafür braucht das Environment `release` das Secret `NPM_STAGE_TOKEN` (Granular Token, „Read and write (stage only)“, Scope `@blackforestsentinel`). Online geht das Paket nach Freigabe mit 2FA: `npm stage list`, dann `npm stage approve <id>`, oder auf npmjs.com. Danach den Trusted Publisher einrichten und das Token löschen.
- **GitHub:** Environment `release` mit Schutzregel, damit nur Tags veröffentlichen.

## Lizenz

MIT, siehe [LICENSE](LICENSE).
