# Sentinel Seed – Code-Pakete

Code-Bausteine für Projekte aus dem [Sentinel-Seed-Template](https://github.com/blackforestsentinel/seed-template). Projekte ziehen sie als Dependency mit fester Version, statt Code zu kopieren.

| Paket | Registry | Inhalt |
| --- | --- | --- |
| [`Bfs.Seed.Functions.Core`](dotnet/src/Bfs.Seed.Functions.Core) | nuget.org | `AddSeedCore()`: Application Insights, `SeedOptions`, `SeedHealthReport`, `SeedSecrets` mit Prüfung beim Start |
| [`@blackforestsentinel/seed-web-core`](web/packages/seed-web-core) | npm | `loadRuntimeConfig()`, `createHttpClient()` |
| [`Bfs.Seed.Auth`](dotnet/src/Bfs.Seed.Auth) | nuget.org | `UseSeedAuth()`: Token-Prüfung gegen Entra ID, `[AllowAnonymous]` für Ausnahmen |
| [`@blackforestsentinel/seed-web-auth`](web/packages/seed-web-auth) | npm | `createSeedAuth()`: Login per MSAL, Access-Tokens für die API und für Microsoft Graph |
| [`@blackforestsentinel/seed-web-people`](web/packages/seed-web-people) | npm | React-Komponenten für Personen: `Avatar`, `CurrentUserMenu`, `PeoplePicker`, `PersonCard`, `SignInPage`, `AccessDenied`, Daten aus Microsoft Graph im Browser |
| [`Bfs.Seed.Mcp`](dotnet/src/Bfs.Seed.Mcp) | nuget.org | `AddSeedMcp()`: MCP-Server unter `/api/mcp` mit Metadaten nach RFC 9728, Werkzeuge per Attribut, Capabilities je Werkzeug |
| [`Bfs.Seed.Storage`](dotnet/src/Bfs.Seed.Storage) | nuget.org | `AddSeedStorage()`: Clients für Table, Blob und Queue (Managed Identity, lokal Azurite), Table-Repository mit ETag, Partition je `oid`/`tid`, Queue-Nachrichten passend zum Trigger |
| [`Bfs.Seed.OpenApi`](dotnet/src/Bfs.Seed.OpenApi) | nuget.org | openapi.json (Swagger 2.0) beim Build aus Attributen an den HTTP-Functions, ohne Functions-Host; Grundlage für den Custom Connector der Power Platform |
| [`Bfs.Seed.Extension`](dotnet/src/Bfs.Seed.Extension) | nuget.org | `AddSeedExtensionUpdates()`: Update-Endpunkt einer selbst gehosteten Browser-Erweiterung (`/api/extension/updates.xml`, signierte CRX aus dem Container `extension`), für die Verteilung per Intune |

`Bfs.Seed.OpenApi` wirkt beim Build: Das Paket bringt neben der Bibliothek ein Export-Werkzeug (`tools/`) und ein MSBuild-Target (`build/`) mit. Das Werkzeug ist kein eigenes Paket; sein Projekt `Bfs.Seed.OpenApi.Export` ist nicht packbar.

## Entwickeln

```bash
cd dotnet && dotnet test --solution Bfs.Seed.slnx
cd web && npm install && npm run build && npm test
```

Die Integrationstests von `Bfs.Seed.Storage` laufen gegen Azurite (`npm install -g azurite`, dann `azurite --inMemoryPersistence --skipApiVersionCheck`). Ohne Azurite werden sie übersprungen, in der CI (`SEED_REQUIRE_AZURITE=true`) schlagen sie fehl.

## Veröffentlichen

Alle Pakete erscheinen gemeinsam in der Version des Git-Tags. Im Repo stehen Versionen und Abhängigkeiten der npm-Pakete untereinander auf `0.0.0-dev`; [`web/scripts/set-version.mjs`](web/scripts/set-version.mjs) setzt sie im Workflow auf die Version des Tags. Veröffentlicht wird nur aus [`release.yml`](.github/workflows/release.yml), nie lokal:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Der Tag muss auf einem Commit liegen, der schon auf `main` ist; sonst bricht der Workflow ab, bevor er baut. Die Actions in den Workflows sind auf den Commit gepinnt, Updates schlägt Dependabot vor ([`.github/dependabot.yml`](.github/dependabot.yml)).

Der Workflow authentifiziert sich per Trusted Publishing (OIDC) bei nuget.org und npm. Einrichtung:

- **nuget.org:** Trusted-Publishing-Richtlinie für `blackforestsentinel/seed-packages`, Workflow `release.yml`, Environment `release`. Variable `NUGET_USER` im Environment `release` mit dem nuget.org-Benutzernamen.
- **npm:** Trusted Publisher je Paket auf npmjs.com (`seed-packages`, `release.yml`, Environment `release`, „Allow npm publish“). Eine neue Verbindung verfällt nach 48 Stunden, wenn bis dahin keine Veröffentlichung über sie läuft.
- **Neues npm-Paket:** Trusted Publishing lässt sich erst einrichten, wenn das Paket existiert. Der Workflow erkennt neue Pakete und stellt sie per `npm stage publish` bereit; dafür braucht das Environment `release` das Secret `NPM_STAGE_TOKEN` (Granular Token, „Read and write (stage only)“, Scope `@blackforestsentinel`). Online geht das Paket nach Freigabe mit 2FA: `npm stage list`, dann `npm stage approve <id>`, oder auf npmjs.com. Danach den Trusted Publisher einrichten und das Token löschen.
- **GitHub:** Environment `release` mit Schutzregel, damit nur Tags veröffentlichen.

## Sicherheit

Schwachstellen bitte vertraulich melden, nicht als Issue: siehe [SECURITY.md](SECURITY.md).

## Lizenz

MIT, siehe [LICENSE](LICENSE).
