# Bfs.Seed.Mcp

MCP-Server für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template). Claude, VS Code und andere KI-Werkzeuge rufen damit im Namen der angemeldeten Person Werkzeuge der API auf. Basis ist das offizielle C#-SDK `ModelContextProtocol.Core`.

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.UseSeedAuth();
builder.AddSeedMcp()
    .WithToolsFromAssembly(typeof(Program).Assembly)
    .WithInstructions("Rufe zuerst ueberblick_abrufen auf.");
builder.Build().Run();
```

```csharp
[McpServerToolType]
public sealed class RechnungTools(RechnungService rechnungen)
{
    [McpServerTool(Name = "rechnung_abrufen", ReadOnly = true), Description("Liefert eine Rechnung mit ihren Positionen.")]
    [RequireCapability("invoices.read")]
    public Task<Rechnung> AbrufenAsync(ClaimsPrincipal user, [Description("Rechnungsnummer, z. B. R-2026-001")] string nummer) =>
        rechnungen.AbrufenAsync(user, nummer);
}
```

## Was das Paket mitbringt

- **Endpunkt `/api/mcp`** als HTTP-Function im Paket, Streamable HTTP und zustandslos: Jede Anfrage bekommt einen eigenen Server, ohne Sitzung und ohne Ereignisstrom (`GET` und `DELETE` antworten `405`). So verteilt Flex Consumption die Aufrufe auf beliebig viele Instanzen, und `tools/list` und `tools/call` funktionieren auch ohne vorheriges `initialize`.
- **Metadaten nach RFC 9728** unter `/api/.well-known/oauth-protected-resource`, ohne Anmeldung: `resource`, Entra-Tenant als `authorization_servers`, `scopes_supported`. `AddSeedMcp()` setzt `SeedAuthOptions.ResourceMetadataPath`; damit nennt jede 401-Antwort der Token-Prüfung die Metadaten als `resource_metadata`. Am Pfad der Wurzel (`/.well-known/…`) können Functions wegen des Routen-Präfixes `api` nichts ausliefern; Claude, Claude Code und VS Code folgen dem Header.
- **Werkzeuge per Attribut** aus dem SDK: `[McpServerToolType]`, `[McpServerTool]`, `[Description]`. Parameter vom Typ `ClaimsPrincipal` bekommen die angemeldete Person, Dienste aus der Dependency Injection kommen über Konstruktor oder Parameter. Je Anfrage gilt der Scope der Function-Ausführung.
- **Prüfung der Argumente** gegen das Schema, das aus den Parametern entsteht: Typen, Pflichtangaben, Aufzählungen, Längen, Grenzen, Muster, auch verschachtelt. Unbekannte Argumente lehnt der Endpunkt ab, statt sie still zu ignorieren. Fehler gehen als Werkzeugergebnis mit `isError` an das Modell, damit es den Aufruf korrigieren kann.
- **Capabilities je Werkzeug** über `[RequireCapability]` aus `Bfs.Seed.Auth` an Methode oder Klasse; mehrere müssen alle erfüllt sein. `tools/list` zeigt nur Werkzeuge, deren Capabilities die Person hat. `tools/call` auf ein anderes liefert ein Ergebnis mit `isError` und der fehlenden Berechtigung, ein unbekanntes Werkzeug einen JSON-RPC-Fehler.

## Anmeldung und Konfiguration

Die Token-Prüfung macht `Bfs.Seed.Auth`: Der MCP-Endpunkt verlangt per `[RequireScope("mcp_access")]` den Scope `mcp_access`, alle anderen Functions weiter `access_as_user`. Eine Liste zugelassener Clients gibt es nicht; jedes gültige Token für die API mit `mcp_access` wird angenommen. Ohne Feature sso (`Seed:Features:Sso`) startet die App mit `AddSeedMcp()` nicht, und ohne angemeldete Person antwortet der Endpunkt immer mit `401`.

| Einstellung | App-Setting | Bedeutung |
| --- | --- | --- |
| `Mcp:Resource` | `Mcp__Resource` | Kanonische Adresse auf der eigenen Domain, z. B. `https://mcp.example.org/api/mcp`; setzt das Terraform-Modul `sso` mit `mcp_custom_domain`. Kommt eine Anfrage über diese Domain, nennen die Metadaten sie als `resource` und den Scope `<Adresse>/mcp_access`; über den Standardnamen der Function gilt `api://<client-id>/mcp_access`. |
| `Mcp:ServerName` | `Mcp__ServerName` | Name in `initialize` und in den Metadaten, Standard `Seed:Project`. |
| `Mcp:Instructions` | `Mcp__Instructions` | Hinweise an das Modell; `WithInstructions(...)` hat Vorrang. |

Claude und Claude Code senden die MCP-Adresse als `resource`; Entra stellt dann nur ein Token aus, wenn sie Application ID URI der API ist, und das geht nur auf einer im Tenant verifizierten Domain. Einrichtung und Verbinden der Clients beschreibt die README von [seed-template](https://github.com/blackforestsentinel/seed-template).
