# Bfs.Seed.Extension

Update-Endpunkt für eine selbst gehostete Browser-Erweiterung (Edge, Chrome) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template). Verwaltete Geräte installieren die Erweiterung per Intune-Richtlinie (`ExtensionInstallForcelist`) aus `/api/extension/updates.xml` und holen sich dort neue Versionen, ohne Store.

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.UseSeedAuth();
builder.AddSeedStorage();
builder.AddSeedExtensionUpdates();
builder.Build().Run();
```

## Was das Paket mitbringt

- **`GET /api/extension/updates.xml`**: Update-Manifest im gupdate-Format (Protokoll 2.0) mit ID, Version, Paket-Adresse und SHA-256 der aktuellen Veröffentlichung, `Cache-Control: no-cache`. 404, solange nichts veröffentlicht ist.
- **`GET /api/extension/packages/{file}`**: das signierte CRX, `Cache-Control: immutable`. Nur Namen `<name>-<version>.crx`, wie die Pipeline sie vergibt; alles andere, auch `current.json` oder Pfade mit `../`, beantwortet der Endpunkt mit 404. Ältere Pakete bleiben abrufbar, damit ein laufender Download nicht ins Leere geht.
- **Ohne Anmeldung**, wie der Browser es verlangt (`[AllowAnonymous]` für `Bfs.Seed.Auth`). Das CRX ist signiert und sein Inhalt nicht geheim; die Erweiterung meldet sich erst danach selbst an.
- **Functions im Paket:** Sie kommen in jede App, die das Paket referenziert; ohne `AddSeedExtensionUpdates()` antworten beide mit 404. In das openapi.json von `Bfs.Seed.OpenApi` gehen sie nicht ein.

## Woher die Pakete kommen

Die Seed-Pipeline (seed-pipelines, Parameter `extension` mit `signing`) signiert das Paket im Deploy der Umgebung als CRX3 und legt im Container `extension` des Projekt-Storage zuerst `<name>-<version>.crx` und danach `current.json` ab:

```json
{ "id": "…", "version": "1.0.0.2210", "file": "kundenportal-1.0.0.2210.crx", "sha256": "…" }
```

Weitere Felder der Datei werden ignoriert. Jede Instanz hält `current.json` eine Minute im Speicher; Browser fragen ohnehin nur alle paar Stunden.

## Konfiguration

| Einstellung | App-Setting | Standard | Bedeutung |
| --- | --- | --- | --- |
| `Extension:PackageName` | `Extension__PackageName` | `Seed:Project` | Präfix der Paketnamen, wie `extension.name` in azure-pipelines.yml |
| `Extension:ContainerName` | `Extension__ContainerName` | `extension` | Container mit den Paketen (`storage.containers` in project.yaml) |
| `Extension:CacheDuration` | `Extension__CacheDuration` | `00:01:00` | Wie lange eine Instanz `current.json` im Speicher hält, höchstens eine Stunde |

Voraussetzungen: Feature storage (`Seed:Features:Storage`, sonst startet die App nicht) und der Container `extension` in `storage.containers`. Die Function-Identität liest ihn mit den Rechten aus dem Modul `storage`.
