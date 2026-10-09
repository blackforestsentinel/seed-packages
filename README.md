# Sentinel Seed – Code-Pakete

Code-Bausteine für Projekte aus dem [Sentinel-Seed-Template](https://github.com/blackforestsentinel/seed-template). Projekte ziehen sie als Dependency mit fester Version, statt Code zu kopieren.

| Paket | Registry | Inhalt |
| --- | --- | --- |
| [`Bfs.Seed.Functions.Core`](dotnet/src/Bfs.Seed.Functions.Core) | nuget.org | `AddSeedCore()`: Application Insights, `SeedOptions`, `SeedHealthReport` |
| [`@blackforestsentinel/seed-web-core`](web/packages/seed-web-core) | npm | `loadRuntimeConfig()`, `createHttpClient()` |

Geplant: `Bfs.Seed.Auth` und `@blackforestsentinel/seed-web-auth` (SSO), `Bfs.Seed.Storage`, `Bfs.Seed.OpenApi`.

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

- **nuget.org:** Trusted-Publishing-Richtlinie für `blackforestsentinel/seed-packages`, Workflow `release.yml`, Environment `release`. Repository-Variable `NUGET_USER` mit dem nuget.org-Benutzernamen.
- **npm:** Trusted Publisher je Paket auf npmjs.com. Ein Paket muss dafür schon existieren; für die Erstveröffentlichung einmalig das Secret `NPM_TOKEN` setzen und danach löschen.
- **GitHub:** Environment `release` mit Schutzregel, damit nur Tags veröffentlichen.

## Lizenz

MIT, siehe [LICENSE](LICENSE).
