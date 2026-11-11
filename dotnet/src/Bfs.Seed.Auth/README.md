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
- Jede HTTP-Function legt fest, wer sie aufrufen darf: `[RequireCapability]`, `[RequireAnyRole]`, `[AllowWithoutRole]` oder `[AllowAnonymous]` (unten, „Wer eine Function aufrufen darf“). Fehlt die Angabe, bricht der Build mit SEEDAUTH001 ab (unten, „Regeln im Build“).
- Die angemeldete Person steht in `request.HttpContext.User`.
- Ohne Token, mit ungültigem Token oder mit dem Token einer nicht zugelassenen Anwendung antwortet die API mit 401, mit gültigem Token ohne passende Berechtigung, Rolle oder Capability mit 403.

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
- Nach der Token-Prüfung löst die Middleware die Rollen aus dem `roles`-Claim in Capabilities auf und hängt sie als Claims `SeedClaimTypes.Capability` an `HttpContext.User`. Es zählen nur Rollen, deren `memberTypes` zum Aufrufer passen. Claims vom Typ `SeedClaimTypes.Capability` oder `SeedClaimTypes.RoleDisplayName`, die schon im Token stehen, verwirft sie vorher; Capabilities kommen nur aus `auth.roles`.
- `[RequireCapability("invoices.write")]` an Methode oder Klasse verlangt eine Capability; mehrere Attribute müssen alle erfüllt sein. `[RequireAnyRole]` verlangt irgendeine Rolle (unten). `User.HasCapability(...)`, `GetCapabilities()`, `GetRoles()` und `GetRoleDetails()` helfen im Code.
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

`[RequireCapability]`, `[RequireScope]`, `[RequireAnyRole]` und `[AllowWithoutRole]` prüft nur die Middleware von `UseSeedAuth()`. Fehlt der Aufruf, etwa weil das Feature sso aus ist, startet eine App mit `AddSeedCore()` nicht, solange eine Function mit einem der Attribute aufrufbar ist; sonst wäre sie ohne Anmeldung erreichbar. Functions, die nur mit sso Sinn ergeben, schaltet das Projekt ohne sso per App-Setting `AzureWebJobs.<Name>.Disabled=true` ab (siehe Bfs.Seed.Functions.Core).

Tippfehler fallen früh auf: Nennt ein `[RequireCapability]` eine Capability, die keine Rolle vergibt, startet die App nicht. Denselben Fehler findet ein Test schon im Build:

```csharp
var map = SeedCapabilityMap.Load(Path.Combine(AppContext.BaseDirectory, "project.yaml"));
Assert.Empty(SeedCapabilityCheck.FindUnknownCapabilities(map, typeof(Program).Assembly));
```

## Wer eine Function aufrufen darf

Ohne Attribut nimmt die Middleware jedes gültige Token an, auch von einem Konto ohne App-Rolle. Mit `auth.assignmentRequired: false` bekommt jedes Konto des Tenants ein Token, Gäste eingeschlossen. Deshalb legt jede HTTP-Function ausdrücklich fest, wer sie aufrufen darf; der Analyzer erzwingt das im Build (SEEDAUTH001). Die Attribute gelten an Methode oder Klasse, auch geerbt.

| Attribut | ohne Token | gültiges Token ohne App-Rolle | mit irgendeiner App-Rolle | mit der Capability |
| --- | --- | --- | --- | --- |
| `[RequireCapability("invoices.write")]` | 401 | 403 | 403, wenn die Rolle die Capability nicht bringt | 200 |
| `[RequireAnyRole]` | 401 | 403 | 200 | 200 |
| `[AllowWithoutRole]` | 401 | 200 | 200 | 200 |
| `[AllowAnonymous]` | 200 | 200 | 200 | 200 |

- **`[RequireAnyRole]`** verlangt mindestens einen Wert im `roles`-Claim, gleich welchen. Entra stellt dort nur App-Rollen dieser API aus. Eine Rolle ohne Capabilities zählt, ebenso eine, die `auth.roles` nicht kennt. App-only-Tokens erfüllen es immer, weil die Token-Prüfung sie nur mit einer Rolle für Anwendungen annimmt. Im lokalen Modus zählen die Rollen aus `Auth:LocalUser:Roles`; ohne sie antwortet die API mit 403.
- **`[AllowWithoutRole]`** ändert nichts am Verhalten ohne Attribut; es macht die Entscheidung sichtbar. Gedacht ist es für Endpunkte, die auch Personen ohne Rolle erreichen müssen, etwa `GET /api/me`: Daran erkennt das Frontend, dass jemand keine Rolle hat, und zeigt „Kein Zugriff“. Ein Token verlangt die API weiter. Das Attribut lockert nichts: Ein `[RequireAnyRole]` oder `[RequireCapability]` an der Klasse gilt trotzdem.
- **`[AllowAnonymous]`** nimmt die Function ganz von der Token-Prüfung aus. Auch ein mitgeschicktes Token prüft die API dann nicht; `HttpContext.User` bleibt leer. Steht daneben an Methode oder Klasse ein anderes Attribut aus der Tabelle oder `[RequireScope]`, wirkt es nicht; der Build bricht mit SEEDAUTH003 ab.
- `[RequireScope]` legt nur fest, mit welcher delegierten Berechtigung eine Anwendung im Namen der Person aufruft, nicht, wer die Person sein darf. Es ersetzt keines der Attribute oben.
- Mit `auth.assignmentRequired: true` bekommt nur ein Token, wer einer Rolle zugewiesen ist, direkt oder über eine Gruppe. Dann wirken `[RequireAnyRole]` und `[AllowWithoutRole]` praktisch gleich.
- Die Middleware liest die Attribute der Methode, deren `[Function]` den Namen der aufgerufenen Function trägt, nicht nur der Methode mit dem Namen aus dem Entry Point. Gleichnamige Methoden einer Klasse (Überladungen) bekommen so nie die Attribute der anderen. Findet sie keine eindeutige Methode, scheitert der Aufruf mit einer `InvalidOperationException`, die die Function nennt, statt ohne Anforderungen durchzulassen. Der Build meldet gleichnamige Functions schon vorher (SEEDAUTH004).

## Regeln im Build (Analyzer)

Das Paket bringt einen Roslyn-Analyzer mit (`analyzers/dotnet/cs/Bfs.Seed.Analyzers.dll`). Er läuft in jedem Projekt, das `Bfs.Seed.Auth` referenziert, direkt oder über `Bfs.Seed.Mcp`; einschalten muss ihn niemand. Ein Testprojekt, das nur das API-Projekt referenziert, bekommt ihn nicht.

- **Fehler, nicht abschaltbar:** Alle Regeln melden einen Fehler, auch ohne `TreatWarningsAsErrors`. Weder `#pragma warning disable` noch `NoWarn`, `dotnet_diagnostic.<ID>.severity` in `.editorconfig` oder ein Ruleset schalten sie ab, auch nicht mit Begründung im Kommentar.
- **Ausnahme nur mit Begründung:** Eine Ausnahme geht allein über `[SuppressMessage]` mit einer Begründung von mindestens 10 Zeichen ohne Leerraum am Rand. So steht sie im Code und im Pull Request:

  ```csharp
  [Function("Webhook")]
  [SuppressMessage("Security", "SEEDAUTH001", Justification = "Prüft die Signatur des Absenders selbst, ohne Token.")]
  public Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest request) { … }
  ```

- **Generierter Code:** Die Regeln zur Anmeldung (SEEDAUTH…) prüfen auch generierten Code. Eine Kopfzeile `// <auto-generated/>`, ein Dateiname `*.g.cs` oder `generated_code = true` in `.editorconfig` nimmt eine Function also nicht aus. Was die Source Generators des Functions-SDK und des MCP-SDK erzeugen, enthält keine Functions oder Werkzeuge und löst nichts aus. Die Regeln zu Unterdrückungen (SEEDSUPPRESS…) lassen generierten Code aus; Generatoren schalten Warnungen oft pauschal ab.

### SEEDAUTH001

**HTTP-Function ohne Berechtigungsangabe** (Kategorie Security). Eine Methode mit `[Function]` und einem Parameter mit `[HttpTrigger]` trägt an Methode oder Klasse (auch geerbt) weder `[RequireCapability]` noch `[RequireAnyRole]`, `[AllowWithoutRole]` oder `[AllowAnonymous]`. Functions mit anderen Triggern (Timer, Queue …) sind ausgenommen, ebenso Projekte ohne Bfs.Seed.Auth. `[RequireScope]` allein genügt nicht.

Abhilfe: das passende Attribut aus der Tabelle oben setzen. Im Zweifel `[RequireCapability]` mit einer Capability aus `auth.roles`.

### SEEDAUTH002

**MCP-Werkzeug ohne Berechtigungsangabe** (Kategorie Security). Eine Methode mit `[McpServerTool]` trägt an Methode oder Klasse weder `[RequireCapability]` noch `[RequireAnyRole]` oder `[AllowWithoutRole]`. Ohne Angabe sieht jedes Konto, das sich am MCP-Endpunkt anmelden kann, das Werkzeug. `[AllowAnonymous]` zählt hier nicht: Der Endpunkt `/api/mcp` verlangt immer ein Token. Die Regel gilt nur in Projekten mit Bfs.Seed.Mcp; ein eigener MCP-Server kennt die Seed-Attribute nicht.

### SEEDAUTH003

**`[AllowAnonymous]` hebt andere Berechtigungsangaben auf** (Kategorie Security). Eine HTTP-Function trägt an Methode oder Klasse (auch geerbt) `[AllowAnonymous]` und zugleich `[RequireCapability]`, `[RequireAnyRole]`, `[AllowWithoutRole]` oder `[RequireScope]`. Die Middleware fragt `[AllowAnonymous]` zuerst ab und prüft dann nichts mehr; die Function wäre ohne Anmeldung erreichbar. Typischer Fall: `[AllowAnonymous]` an der Klasse wegen einer Health-Function, dazu `[RequireCapability]` an einer anderen Methode derselben Klasse.

Abhilfe: `[AllowAnonymous]` an die Methode setzen, die es braucht, oder die anonyme Function in eine eigene Klasse legen.

MCP-Werkzeuge betrifft die Regel nicht: Bfs.Seed.Mcp wertet `[AllowAnonymous]` an Werkzeugen nicht aus, deren Capabilities gelten weiter.

### SEEDAUTH004

**Mehrere Functions mit demselben Methodennamen** (Kategorie Security). Zwei oder mehr Methoden einer Klasse tragen `[Function]` und heißen gleich, etwa überladene `Run`-Methoden für einen HTTP- und einen Queue-Trigger. Der Entry Point einer Function nennt nur Klasse und Methode, für alle diese Functions also denselben. Bfs.Seed.Auth ordnet die Attribute über den Namen in `[Function]` zu; ältere Versionen und andere Werkzeuge, die über den Entry Point gehen, verwechseln die Functions und wenden die Berechtigungsangaben der einen auf die andere an. Die Regel gilt für alle Trigger.

Abhilfe: jeder Function eine Methode mit eigenem Namen geben.

### SEEDSUPPRESS001

**Unterdrückung ohne Begründung** (Kategorie Maintainability). `[SuppressMessage]` oder `[UnconditionalSuppressMessage]` ohne `Justification` oder mit einer Begründung unter 10 Zeichen, gezählt ohne Leerraum am Rand. Der Platzhalter `<Pending>`, den Visual Studio einträgt, genügt also nicht. Das gilt überall, auch für `[assembly: SuppressMessage(…)]` in `GlobalSuppressions.cs` und unter einem Alias.

Abhilfe: `Justification = "…"` mit dem Grund, warum die Meldung hier nicht zutrifft. Eine Konstante ist erlaubt.

### SEEDSUPPRESS002

**`#pragma warning disable` ohne Begründung** (Kategorie Maintainability). Jedes `#pragma warning disable` braucht in derselben Zeile einen `//`-Kommentar mit mindestens 10 Zeichen, ohne Leerraum am Rand:

```csharp
#pragma warning disable CA2000 // Der Aufrufer gibt den Stream frei.
```

Ein Kommentar in der Zeile davor zählt nicht, ebenso wenig `/* … */`: C# erlaubt nach einer Direktive nur `//`-Kommentare. `#pragma warning restore` braucht keine Begründung. Für die SEED-Regeln selbst wirkt ein Pragma nicht (oben).

Auch diese beiden Regeln lassen sich nur per `[SuppressMessage]` mit Begründung abschalten, etwa für Altcode beim Umzug in Seed:

```csharp
[assembly: SuppressMessage("Maintainability", "SEEDSUPPRESS002", Justification = "Altcode aus dem Vorgänger, Begründungen folgen je Datei.")]
```

**Grenzen:** Roslyn wendet `[SuppressMessage]` auch auf nicht konfigurierbare Regeln an. Eine Unterdrückung von SEEDSUPPRESS001 selbst hebt deshalb auch die Meldung über ihre fehlende Begründung auf; sie steht dann aber mit dieser ID im Diff. Nicht erfasst sind andere Regeln, die `NoWarn` in der Projektdatei oder `.editorconfig` projektweit abschalten; solche Änderungen stehen ebenfalls im Diff.

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
