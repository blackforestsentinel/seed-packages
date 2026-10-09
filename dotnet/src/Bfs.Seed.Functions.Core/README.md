# Bfs.Seed.Functions.Core

Grundausstattung für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template).

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.Build().Run();
```

`AddSeedCore()` registriert Application Insights und bindet `SeedOptions` an die App-Settings `Seed__Project` und `Seed__Environment`, die das Terraform-Modul `core` setzt. `SeedHealthReport.Create(...)` liefert die Antwort für den Health-Endpunkt, den der Smoke-Test der Pipeline aufruft.
