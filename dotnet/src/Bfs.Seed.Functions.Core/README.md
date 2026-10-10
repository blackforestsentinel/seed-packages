# Bfs.Seed.Functions.Core

Grundausstattung für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template).

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.Build().Run();
```

`AddSeedCore()` registriert Application Insights und bindet `SeedOptions` an die App-Settings `Seed__Project` und `Seed__Environment`, die das Terraform-Modul `core` setzt. `SeedHealthReport.Create(...)` liefert die Antwort für den Health-Endpunkt, den der Smoke-Test der Pipeline aufruft.

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
