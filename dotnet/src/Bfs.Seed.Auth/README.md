# Bfs.Seed.Auth

Token-Prüfung für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template). Browser (MSAL), Custom Connector, MCP-Clients und Dienste rufen die API mit einem Bearer-Token für dieselbe API-App-Registrierung auf; eine Middleware prüft alle.

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.UseSeedAuth();
builder.Build().Run();
```

- Alle HTTP-Functions verlangen ein gültiges Token: Signatur gegen die Schlüssel des Tenants, Aussteller, Audience, Laufzeit und die delegierte Berechtigung `access_as_user`. v1- und v2-Tokens gelten beide, weil eine vorhandene Registrierung im Kunden-Tenant v1-Tokens ausstellen kann. Mit `Auth:AllowedClientIds` muss ein delegiertes Token außerdem von einer zugelassenen Anwendung kommen (unten, „Zugelassene Clients“).
- Ausnahmen markiert `[AllowAnonymous]` (aus `Microsoft.AspNetCore.Authorization`) an Methode oder Klasse, etwa für den Health-Endpunkt.
- Die angemeldete Person steht in `request.HttpContext.User`.
- Ohne Token, mit ungültigem Token oder mit dem Token einer nicht zugelassenen Anwendung antwortet die API mit 401, mit gültigem Token ohne passende Berechtigung oder Capability mit 403.

Die Einstellungen kommen aus dem Terraform-Modul `sso`: `Auth__TenantId`, `Auth__ClientId`, `Auth__Audience`, bei einer anderen delegierten Berechtigung als `access_as_user` auch `Auth__RequiredScope`. Fehlen sie, startet die App nicht. Dazu kommen die zugelassenen Clients in `Auth__AllowedClientIds__0`, `__1`, …

## Rollen und Capabilities

Rechte hängen an Capabilities wie `invoices.write`, nicht direkt an Rollen. Welche App-Rolle welche Capabilities bringt, steht an einer Stelle: `auth.roles` in der `project.yaml` des Projekts.

```yaml
auth:
  roles:
    Reader:
      description: Liest Rechnungen
      capabilities: [invoices.read]
    Admin:
      displayName: Administrator         # Standard: der Rollenname
      description: Verwaltet Rechnungen
      memberTypes: [User, Application]   # Standard: [User]
      capabilities: [invoices.read, invoices.write]
```

- Terraform legt aus derselben Datei die App-Rollen an der API-Registrierung an. Personen und Gruppen weist ein Admin in Entra den Rollen zu.
- Nach der Token-Prüfung löst die Middleware die Rollen aus dem `roles`-Claim in Capabilities auf und hängt sie als Claims `SeedClaimTypes.Capability` an `HttpContext.User`. Es zählen nur Rollen, deren `memberTypes` zum Aufrufer passen.
- `[RequireCapability("invoices.write")]` an Methode oder Klasse verlangt eine Capability; mehrere Attribute müssen alle erfüllt sein. `User.HasCapability(...)`, `GetCapabilities()`, `GetRoles()` und `GetRoleDetails()` helfen im Code.
- Eine Rolle darf ohne Capabilities sein (`capabilities: []` oder ohne den Schlüssel), etwa als Zielgruppe, die nur festlegt, welche Inhalte jemand sieht. Abfragen lässt sie sich mit `User.IsInRole("Team.Vertrieb")` oder `GetRoles()`; das Frontend sieht sie in `roles` von `GET /api/me`. Gruppen den Rollen zuzuweisen setzt in Entra ID P1 voraus.
- `SeedUserInfo.From(User)` liefert Name, Benutzername, `oid`, `tid`, Rollen, Rollen mit Anzeigenamen und Capabilities, die Antwort für `GET /api/me`. Das Frontend blendet damit UI-Elemente ein oder aus, ohne die Zuordnung zu kennen. Den Endpunkt legt das Projekt selbst an (im Template `Me/MeFunction.cs`), damit er nicht mit vorhandenen Functions kollidiert und erweiterbar bleibt.

### Anzeigenamen der Rollen

`displayName` an einer Rolle ist der Name, den Menschen sehen: Terraform setzt ihn als Anzeigenamen der App-Rolle in Entra, die API gibt ihn in `GET /api/me` mit, etwa für die Rolle im Benutzermenü („Administrator“ statt `Admin`). Ohne `displayName` gilt der Rollenname.

```json
{
  "name": "Erika Muster",
  "roles": ["Admin", "Reader"],
  "capabilities": ["invoices.read", "invoices.write"],
  "roleDetails": [
    { "value": "Admin", "displayName": "Administrator" },
    { "value": "Reader", "displayName": "Reader" }
  ]
}
```

- `roleDetails` steht in derselben Reihenfolge wie `roles`; alle bisherigen Felder bleiben unverändert.
- Die Token-Prüfung und der lokale Modus hängen die Anzeigenamen als Claims `SeedClaimTypes.RoleDisplayName` an, `GetRoleDetails()` liest sie. Ein Endpunkt mit `SeedUserInfo.From(User)` braucht dafür keine Änderung.
- Eine Rolle, die `auth.roles` nicht kennt, und ein Principal ohne Token-Prüfung (etwa in einem Test) bekommen den Rollennamen als Anzeigenamen.

Die API liest die Zuordnung aus `project.yaml` im Ausgabeordner der App (`Auth:ProjectFile`, Standard `project.yaml`). Das Template verlinkt die Datei dorthin:

```xml
<None Include="..\..\project.yaml" Link="project.yaml" CopyToOutputDirectory="PreserveNewest" />
```

So gilt lokal, in Tests und in Azure dieselbe Datei, und eine geänderte Zuordnung braucht nur einen Deploy, keine Infrastruktur-Freigabe. Fehlt die Datei oder der Abschnitt, ist die Zuordnung leer und alles läuft wie bisher.

`[RequireCapability]` und `[RequireScope]` prüft nur die Middleware von `UseSeedAuth()`. Fehlt der Aufruf, etwa weil das Feature sso aus ist, startet eine App mit `AddSeedCore()` nicht, solange eine Function mit einem der Attribute aufrufbar ist; sonst wäre sie ohne Anmeldung erreichbar. Functions, die nur mit sso Sinn ergeben, schaltet das Projekt ohne sso per App-Setting `AzureWebJobs.<Name>.Disabled=true` ab (siehe Bfs.Seed.Functions.Core).

Tippfehler fallen früh auf: Nennt ein `[RequireCapability]` eine Capability, die keine Rolle vergibt, startet die App nicht. Denselben Fehler findet ein Test schon im Build:

```csharp
var map = SeedCapabilityMap.Load(Path.Combine(AppContext.BaseDirectory, "project.yaml"));
Assert.Empty(SeedCapabilityCheck.FindUnknownCapabilities(map, typeof(Program).Assembly));
```

## Scopes je Function

`[RequireScope("mcp_access", "access_as_user")]` an Methode oder Klasse ersetzt für diese Function die delegierte Berechtigung `Auth:RequiredScope`; eine der genannten genügt. Die Methode geht der Klasse vor. Fehlt sie im Token, antwortet die API mit 403 und `WWW-Authenticate: Bearer error="insufficient_scope", scope="…"`.

## Aufrufe ohne angemeldete Person

Dienste und Automatisierungen rufen die API mit einem App-only-Token auf (Client-Credentials-Flow, ohne `scp`). Die Middleware nimmt es nur an, wenn es eine App-Rolle trägt, die `auth.roles` mit `memberTypes: [Application]` (oder beiden) vorsieht; sonst antwortet sie mit 403. Die Capabilities löst sie genauso auf. Scopes gelten nur für delegierte Tokens, App-only-Tokens prüft die API allein über ihre Rollen. `User.IsApplication()` unterscheidet die beiden Fälle.

Die aufrufende Anwendung trägt die Rolle in ihrer eigenen Registrierung unter „API-Berechtigungen“ als Anwendungsberechtigung der API ein; ein Admin erteilt dafür die Einwilligung. Eine Managed Identity bekommt die Rolle per App-Rollen-Zuweisung über Graph.

## Zugelassene Clients

Ohne weitere Einstellung nimmt die API jedes delegierte Token mit passender Audience und Berechtigung an, gleich welche Anwendung es geholt hat. Holen kann es jede Anwendung, der im Tenant jemand die Berechtigung erteilt hat. Mit `Auth:AllowedClientIds` gilt das nur für die genannten Anwendungen:

```json
"Auth__AllowedClientIds__0": "<Client-ID der SPA>",
"Auth__AllowedClientIds__1": "<Client-ID des Custom Connectors>"
```

- Verglichen wird die Client-ID der aufrufenden Anwendung, `azp` in v2-Tokens und `appid` in v1-Tokens, als GUID ohne Rücksicht auf Groß- und Kleinschreibung. Ein Token ohne beide Claims scheitert.
- Das Token einer anderen Anwendung lehnt die API mit 401 und `error="invalid_token"` ab, wie ein ungültiges Token und noch vor der Prüfung des Scopes. Das Log nennt die Client-ID, damit eine vergessene Anwendung schnell auffällt.
- App-only-Tokens betrifft die Liste nicht; Dienste lässt die API weiter allein über ihre App-Rollen zu (oben).
- Ohne Liste oder nur mit leeren Einträgen entfällt die Prüfung wie bisher. Ein Eintrag, der keine GUID ist, verhindert den Start.
- Das Terraform-Modul `sso` aus seed-terraform füllt die Liste: SPA, Custom Connector, MCP-Client und die vorab zugelassenen MCP-Clients. Mit einer vorhandenen Registrierung in einem anderen Tenant setzt das Projekt die Liste selbst oder lässt sie leer.

## Lokaler Modus

Mit `Auth:Mode = Local` gibt es keinen Token-Zwang: Jede Anfrage läuft als fester Entwicklungsnutzer, dessen Rollen konfigurierbar sind. Die Capabilities folgen aus `auth.roles` wie in Azure.

```json
"Seed__Features__Sso": "true",
"Auth__Mode": "Local",
"Auth__LocalUser__Roles__0": "Admin"
```

Weitere Einstellungen: `Auth__LocalUser__Name`, `__Username`, `__ObjectId`, `__TenantId`. Läuft die App in Azure, startet sie in diesem Modus nicht. Das erkennt sie an Variablen, die nur die Plattform setzt: `WEBSITE_INSTANCE_ID`, `WEBSITE_SITE_NAME`, `WEBSITE_SKU`, `WEBSITE_POD_NAME`, `CONTAINER_NAME` oder `CONTAINER_APP_NAME`. Jeder Hosting-Plan setzt nur einen Teil davon, Flex Consumption etwa kein `WEBSITE_INSTANCE_ID`. Ebenso startet die App nicht, wenn eine lokale Rolle in `auth.roles` fehlt. Im Frontend schaltet `config.json` mit `"auth": { "mode": "local" }` MSAL ab.

## MCP und Protected Resource Metadata

Mit `Auth:ResourceMetadataPath` (z. B. `/.well-known/oauth-protected-resource`) nennt jede 401- und 403-Antwort die Metadaten nach RFC 9728 im Header `WWW-Authenticate` als `resource_metadata`. MCP-Clients finden darüber den Weg zur Anmeldung.

- **Ohne Token:** Die 401 enthält keinen Fehlercode, nach RFC 6750 (Abschnitt 3.1).
- **Scope:** Eine registrierte Umsetzung von `ISeedAuthChallengeScope` nennt für eine Anfrage den voll qualifizierten Scope, den 401 und 403 als `scope` angeben. Bfs.Seed.Mcp tut das für `/api/mcp`, etwa `scope="https://mcp.example.org/api/mcp/mcp_access offline_access"`.

Beispiel für den MCP-Endpunkt ohne Token:

```
WWW-Authenticate: Bearer scope="https://mcp.example.org/api/mcp/mcp_access offline_access", resource_metadata="https://mcp.example.org/api/.well-known/oauth-protected-resource"
```
