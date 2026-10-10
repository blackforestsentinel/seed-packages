# Bfs.Seed.Auth

Token-Prüfung für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template). Browser (MSAL) und Custom Connector rufen die API mit einem Bearer-Token für dieselbe API-App-Registrierung auf; eine Middleware prüft beide.

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.UseSeedAuth();
builder.Build().Run();
```

- Alle HTTP-Functions verlangen ein gültiges Token: Signatur gegen die Schlüssel des Tenants, Aussteller, Audience, Laufzeit und die delegierte Berechtigung `access_as_user`.
- Ausnahmen markiert `[AllowAnonymous]` (aus `Microsoft.AspNetCore.Authorization`) an Methode oder Klasse, etwa für den Health-Endpunkt.
- Die angemeldete Person steht in `request.HttpContext.User`.

Die Einstellungen kommen aus dem Terraform-Modul `sso`: `Auth__TenantId`, `Auth__ClientId`, `Auth__Audience`. Fehlen sie, startet die App nicht.
