# Bfs.Seed.Functions.Core

Grundausstattung für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template).

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.Build().Run();
```

`AddSeedCore()` registriert Application Insights und bindet `SeedOptions` an die App-Settings `Seed__Project` und `Seed__Environment`, die das Terraform-Modul `core` setzt. `SeedHealthReport.Create(...)` liefert die Antwort für den Health-Endpunkt, den der Smoke-Test der Pipeline aufruft.

**Geschützte Functions nur mit Token-Prüfung:** Trägt eine Function `[RequireCapability]`, `[RequireScope]`, `[RequireAnyRole]` oder `[AllowWithoutRole]` aus Bfs.Seed.Auth, startet die App nur, wenn `Program.cs` auch `UseSeedAuth()` aufruft. Die Attribute prüft allein dessen Middleware; ohne sie wäre die Function ohne Anmeldung erreichbar. Die Meldung beim Start nennt die betroffenen Functions. Abgeschaltete Functions zählen nicht: Ein Projekt ohne sso schaltet solche Functions per App-Setting `AzureWebJobs.<Name>.Disabled=true` ab, wie das Template seine Beispiel-Function `Settings`.

## Secrets aus dem Key Vault

Das Terraform-Modul `keyvault` setzt je Secret das App-Setting `Secrets__<Name>` als Key-Vault-Referenz; die Plattform ersetzt sie beim Start der App durch den Wert. Aus dem Secret-Namen wird der Setting-Name, indem jedes Wort groß beginnt und die Bindestriche entfallen:

| Secret (project.yaml, Key Vault) | App-Setting | Konfiguration |
| --- | --- | --- |
| `stripe-key` | `Secrets__StripeKey` | `Secrets:StripeKey` |
| `openai-api-key` | `Secrets__OpenaiApiKey` | `Secrets:OpenaiApiKey` |

`AddSeedCore()` registriert dafür `SeedSecrets`:

```csharp
public sealed class PaymentFunction(SeedSecrets secrets)
{
    [Function("Pay")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest request)
    {
        var client = new StripeClient(secrets.Get("stripe-key"));
        // ...
    }
}
```

`Get()` erst beim Gebrauch aufrufen, nicht in `Program.cs`. Fehlt der Wert, wirft es eine `InvalidOperationException`, die Secret und App-Setting nennt, nie den Wert:

- App-Setting fehlt: Der Name steht nicht unter `keyVault.secrets` in `project.yaml`.
- Platzhalter (`seed-placeholder:<name>`): Terraform hat das Secret angelegt, aber noch niemand den Wert gesetzt.
- Nicht aufgelöste Referenz (der Wert beginnt noch mit `@Microsoft.KeyVault(`): Das Secret fehlt im Vault, oder die Identität der Function hat keine Leserechte.

Die Plattform löst Referenzen auf, wenn sich App-Settings ändern, bei jedem Deploy und auf Anforderung (`…/config/configreferences/appsettings/refresh`, siehe Modul `keyvault`); sonst erst nach bis zu 24 Stunden. `az functionapp restart` reicht auf Flex Consumption nicht.

**Prüfung beim Start:** Beim Start durchsucht `AddSeedCore()` alle App-Settings, auch eigene außerhalb von `Secrets`, nach Platzhaltern (Warning im Log) und nicht aufgelösten Referenzen (Error). Die App startet trotzdem. Beim ersten Deploy hat noch niemand die Werte gesetzt, und die Pipeline braucht danach den Health-Endpunkt für den Smoke-Test. Endpunkte ohne Secrets bleiben nutzbar.

**Health-Report:** `SeedHealthReport.Create(options, assembly, secrets)` meldet dieselben Funde mit `status: "degraded"` und nur den Namen, bei HTTP 200:

```json
{
  "status": "degraded",
  "project": "kundenportal",
  "environment": "dev",
  "version": "20261010.1",
  "secrets": [{ "setting": "Secrets__StripeKey", "secret": "stripe-key", "state": "placeholder" }]
}
```

`state` ist `placeholder` oder `unresolved`. Ohne Funde bleibt `status` `ok` und `secrets` leer.

**Lokal:** Werte für `Secrets__StripeKey` usw. in `local.settings.json` eintragen (nicht eingecheckt).

### Ablauf von Secrets

Trägt ein Secret im Key Vault ein Ablaufdatum (`az keyvault secret set … --expires 2027-06-30T00:00:00Z` oder im Portal), nennt der Health-Report es rechtzeitig. Ohne Ablaufdatum passiert nichts; Seed erzwingt keines. Den Alarm vor Ablauf richtet seed-terraform ein; der Report ergänzt ihn.

```csharp
[Function("Health")]
public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request, CancellationToken cancellationToken)
    => new OkObjectResult(await SeedHealthReport.CreateAsync(options.Value, typeof(HealthFunction).Assembly, secrets, cancellationToken));
```

`SeedHealthReport.CreateAsync(…)` meldet zusätzlich zu `Create(…)` jedes Secret aus `keyVault.secrets`, dessen aktuelle Version in den nächsten 30 Tagen abläuft (`expiring`) oder abgelaufen ist (`expired`), mit dem Datum in `expiresOn`. Der Status ist dann `degraded`, die Antwort weiter 200:

```json
{
  "status": "degraded",
  "project": "kundenportal",
  "environment": "prod",
  "version": "20261105.1",
  "secrets": [{ "setting": "Secrets__StripeKey", "secret": "stripe-key", "state": "expiring", "expiresOn": "2026-11-30T00:00:00+00:00" }]
}
```

- **Nur bald ablaufende Secrets:** Der Health-Endpunkt ist anonym. Er nennt Namen und Datum deshalb erst 30 Tage vor Ablauf, wie heute die Namen fehlender Secrets, also nur, wenn ohnehin jemand handeln muss. Eine vollständige Liste aller Ablaufdaten verriete dauerhaft, welche Dienste das Projekt anbindet und wann deren Zugänge enden; das hilft etwa bei glaubwürdigen Phishing-Mails. Alle Ablaufdaten liefert `secrets.GetExpirationsAsync()`, gedacht für einen Endpunkt oder ein Werkzeug mit `[RequireCapability]`.
- **Woher:** `Seed__KeyVaultUri` mit der Adresse des Vaults (`https://<name>.vault.azure.net/`). Nach dem Start stehen in den App-Settings die Werte der Secrets, nicht mehr die Referenzen; ohne die Adresse kennt die App den Vault nicht. Ohne das Setting fehlen nur die Ablaufdaten, alles andere bleibt wie bisher.
- **Rechte:** keine neuen. Gelesen wird mit der Managed Identity der Function (`AZURE_CLIENT_ID`) über List Secrets, also nur Metadaten, nie Werte. Das deckt die Rolle Key Vault Secrets User ab, die das Modul `keyvault` für die Key-Vault-Referenzen ohnehin vergibt (`secrets/readMetadata/action`). Lokal gilt die eigene Anmeldung (`DefaultAzureCredential`, etwa `az login`).
- **Gezählt** werden Secrets, zu denen es ein App-Setting `Secrets__<Name>` gibt; von Hand angelegte Secrets ohne Setting nicht.
- **Last:** Der Report liest den Vault höchstens einmal je Stunde und Instanz (einen Fehler nach fünf Minuten erneut), gleichzeitige Aufrufe teilen sich einen Abruf. Häufige anonyme Aufrufe erreichen den Vault also nicht. Den ersten Abruf nach einem Kaltstart wartet der Report bis zu fünf Sekunden ab.
- **Fehler:** Antwortet der Vault nicht oder mit einem Fehler, steht in `secrets` ein Eintrag `{ "setting": "Seed__KeyVaultUri", "state": "expiryUnknown" }` und der Status ist `degraded`; die Einzelheiten stehen nur im Log (Warning).
- **Pakete:** Dafür hängt Bfs.Seed.Functions.Core an `Azure.Security.KeyVault.Secrets` und `Azure.Identity` (wie Bfs.Seed.Storage).
