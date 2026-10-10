# Bfs.Seed.Auth

Token-Prüfung für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template). Browser (MSAL), Custom Connector, MCP-Clients und Dienste rufen die API mit einem Bearer-Token für dieselbe API-App-Registrierung auf; eine Middleware prüft alle.

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.UseSeedAuth();
builder.Build().Run();
```

- Alle HTTP-Functions verlangen ein gültiges Token: Signatur gegen die Schlüssel des Tenants, Aussteller, Audience, Laufzeit und die delegierte Berechtigung `access_as_user`. v1- und v2-Tokens gelten beide, weil eine vorhandene Registrierung im Kunden-Tenant v1-Tokens ausstellen kann.
- Ausnahmen markiert `[AllowAnonymous]` (aus `Microsoft.AspNetCore.Authorization`) an Methode oder Klasse, etwa für den Health-Endpunkt.
- Die angemeldete Person steht in `request.HttpContext.User`.
- Ohne Token oder mit ungültigem Token antwortet die API mit 401, mit gültigem Token ohne passende Berechtigung oder Capability mit 403.

Die Einstellungen kommen aus dem Terraform-Modul `sso`: `Auth__TenantId`, `Auth__ClientId`, `Auth__Audience`, bei einer anderen delegierten Berechtigung als `access_as_user` auch `Auth__RequiredScope`. Fehlen sie, startet die App nicht.

## Rollen und Capabilities

Rechte hängen an Capabilities wie `invoices.write`, nicht direkt an Rollen. Welche App-Rolle welche Capabilities bringt, steht an einer Stelle: `auth.roles` in der `project.yaml` des Projekts.

```yaml
auth:
  roles:
    Reader:
      description: Liest Rechnungen
      capabilities: [invoices.read]
    Admin:
      description: Verwaltet Rechnungen
      memberTypes: [User, Application]   # Standard: [User]
      capabilities: [invoices.read, invoices.write]
```

- Terraform legt aus derselben Datei die App-Rollen an der API-Registrierung an. Personen und Gruppen weist ein Admin in Entra den Rollen zu.
- Nach der Token-Prüfung löst die Middleware die Rollen aus dem `roles`-Claim in Capabilities auf und hängt sie als Claims `SeedClaimTypes.Capability` an `HttpContext.User`. Es zählen nur Rollen, deren `memberTypes` zum Aufrufer passen.
- `[RequireCapability("invoices.write")]` an Methode oder Klasse verlangt eine Capability; mehrere Attribute müssen alle erfüllt sein. `User.HasCapability(...)`, `GetCapabilities()` und `GetRoles()` helfen im Code.
- `SeedUserInfo.From(User)` liefert Name, Benutzername, `oid`, `tid`, Rollen und Capabilities, die Antwort für `GET /api/me`. Das Frontend blendet damit UI-Elemente ein oder aus, ohne die Zuordnung zu kennen. Den Endpunkt legt das Projekt selbst an (im Template `Me/MeFunction.cs`), damit er nicht mit vorhandenen Functions kollidiert und erweiterbar bleibt.

Die API liest die Zuordnung aus `project.yaml` im Ausgabeordner der App (`Auth:ProjectFile`, Standard `project.yaml`). Das Template verlinkt die Datei dorthin:

```xml
<None Include="..\..\project.yaml" Link="project.yaml" CopyToOutputDirectory="PreserveNewest" />
```

So gilt lokal, in Tests und in Azure dieselbe Datei, und eine geänderte Zuordnung braucht nur einen Deploy, keine Infrastruktur-Freigabe. Fehlt die Datei oder der Abschnitt, ist die Zuordnung leer und alles läuft wie bisher.

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

## Lokaler Modus

Mit `Auth:Mode = Local` gibt es keinen Token-Zwang: Jede Anfrage läuft als fester Entwicklungsnutzer, dessen Rollen konfigurierbar sind. Die Capabilities folgen aus `auth.roles` wie in Azure.

```json
"Seed__Features__Sso": "true",
"Auth__Mode": "Local",
"Auth__LocalUser__Roles__0": "Admin"
```

Weitere Einstellungen: `Auth__LocalUser__Name`, `__Username`, `__ObjectId`, `__TenantId`. Läuft die App in Azure (`WEBSITE_INSTANCE_ID` gesetzt), startet sie in diesem Modus nicht; ebenso nicht, wenn eine lokale Rolle in `auth.roles` fehlt. Im Frontend schaltet `config.json` mit `"auth": { "mode": "local" }` MSAL ab.

## MCP und Protected Resource Metadata

Mit `Auth:ResourceMetadataPath` (z. B. `/.well-known/oauth-protected-resource`) nennt jede 401- und 403-Antwort die Metadaten nach RFC 9728 im Header `WWW-Authenticate` als `resource_metadata`. MCP-Clients finden darüber den Weg zur Anmeldung.
