; Veröffentlichte Regeln je Release von Bfs.Seed.Auth (Format: Release-Tracking der Roslyn-Analyzer, RS2008).
; Neue oder geänderte Regeln zuerst in AnalyzerReleases.Unshipped.md, beim Release hierher.

## Release 0.10.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SEEDAUTH001 | Security | Error | SeedAuthorizationAnalyzer: HTTP-Function ohne Berechtigungsangabe
SEEDAUTH002 | Security | Error | SeedAuthorizationAnalyzer: MCP-Werkzeug ohne Berechtigungsangabe
SEEDAUTH003 | Security | Error | SeedAuthorizationAnalyzer: [AllowAnonymous] hebt andere Berechtigungsangaben auf
SEEDSUPPRESS001 | Maintainability | Error | SeedSuppressionAnalyzer: [SuppressMessage] ohne Begründung
SEEDSUPPRESS002 | Maintainability | Error | SeedSuppressionAnalyzer: #pragma warning disable ohne Begründung
