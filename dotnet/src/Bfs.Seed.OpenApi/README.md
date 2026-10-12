# Bfs.Seed.OpenApi

openapi.json (Swagger 2.0) für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template). Die Datei entsteht beim Build aus Attributen an den HTTP-Functions, ohne den Functions-Host zu starten. Daraus entsteht der Custom Connector für Power Automate, Power Apps und Copilot Studio.

```xml
<PackageReference Include="Bfs.Seed.OpenApi" Version="x.y.z" />
```

Die Referenz gehört in das Projekt der Function-App, nicht in Testprojekte. Das Paket bringt Attribute, Generator und ein MSBuild-Target mit; auf dem Build-Agent genügen `dotnet` und das Shared Framework von ASP.NET Core, beides Teil des .NET SDK.

## Annotieren

```csharp
// Program.cs, vor den Anweisungen
[assembly: OpenApiInfo("Rechnungen", Description = "Rechnungen anlegen und abrufen.")]
```

```csharp
public sealed class InvoiceFunctions(InvoiceService invoices)
{
    [Function("GetInvoice")]
    [OpenApiOperation("GetInvoice", "Rechnung abrufen", Description = "Rechnung mit Positionen.")]
    [OpenApiParameter("number", Description = "Rechnungsnummer, z. B. R-2026-001", Summary = "Rechnungsnummer")]
    [OpenApiResponse(200, typeof(Invoice), Description = "Rechnung")]
    [OpenApiResponse(404, Description = "Rechnung nicht gefunden")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoices/{number}")] HttpRequest request,
        string number) => ...;
}

/// <param name="Number">Rechnungsnummer.</param>
/// <param name="Note">Freitext, fehlt ohne Angabe.</param>
public sealed record Invoice(
    string Number,
    decimal Total,
    [property: Description("Positionen in Reihenfolge der Eingabe")] IReadOnlyList<InvoiceLine> Lines,
    string? Note);
```

| Attribut | Wirkung |
| --- | --- |
| `[assembly: OpenApiInfo(title)]` | `info.title` (im Connector dessen Name, höchstens 30 Zeichen) und `Description` |
| `[OpenApiOperation(operationId, summary)]` | operationId (interner Name der Aktion), summary (Titel, höchstens 80 Zeichen), `Description` (danach wählt Copilot Studio Aktionen aus), `Visibility` (`x-ms-visibility`) |
| `[OpenApiParameter(name)]` | ergänzt einen Routenparameter (`Description`, `Summary` als `x-ms-summary`, `Visibility`, `Type`) oder legt einen Query-Parameter an (`Type`, Standard string, `Required`) |
| `[OpenApiRequestBody(typeof(T))]` | JSON-Body, `Required` Standard ja |
| `[OpenApiResponse(status)]`, `[OpenApiResponse(status, typeof(T))]` | Antworten, mehrere erlaubt; ohne `Description` der Standardtext des Statuscodes |
| `[OpenApiIgnore]` | nimmt eine Function oder alle einer Klasse heraus |

Was der Generator selbst ermittelt:

- **Pfad und Methoden** aus `[Function]` und `[HttpTrigger]`. Routenparameter auch ohne Attribut, Typ aus dem gleichnamigen Methodenparameter oder dem Constraint (`{id:int}`, `{id:guid}`), immer mit `x-ms-url-encoding: single`. Nimmt eine Function mehrere Methoden an, gibt es je Methode eine Operation (`ItemGet`, `ItemPut`).
- **Anmeldung:** Jede Operation ohne `[AllowAnonymous]` an Methode oder Klasse bekommt `security` mit OAuth 2.0 (Entra ID, Authorization Code). Die URLs sind mandantenneutral (`login.microsoftonline.com/common`); Client-ID, Scope und Tenant setzt der Custom Connector je Umgebung.
- **Nur HTTP-Functions der App-Assembly:** Der MCP-Endpunkt aus `Bfs.Seed.Mcp`, Queue- und Timer-Functions fehlen.
- **Schemas** nach System.Text.Json mit den Web-Standards (camelCase, Zahlen als Zahlen). Objekte stehen unter `definitions` und werden per `$ref` eingebunden, auch rekursive; generische Typen heißen etwa `PageOfInvoice`.
- **Pflichtfelder** sind Konstruktorparameter ohne Standardwert, die nicht nullbar sind, sowie Eigenschaften mit `required` oder `[JsonRequired]`. Nullbare Werte (`string?`, `int?`) sind keine Pflicht und tragen `x-nullable: true`.
- **Beschreibungen** von Eigenschaften: `[Description]` an der Eigenschaft oder am Record-Parameter (`[property: Description("…")]`), sonst die XML-Doku (`<param>` des Records oder primären Konstruktors, `<summary>` der Eigenschaft), wenn das Projekt `<GenerateDocumentationFile>true</GenerateDocumentationFile>` setzt. Die `<summary>` eines Typs bleibt draußen, sie ist meist für Entwickler geschrieben. Gegen die Compiler-Warnungen zu fehlenden Kommentaren: `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>`.

## Wo die Datei landet

- Nach jedem Build unter `bin/<Konfiguration>/net10.0/openapi/openapi.json`. Ohne Änderung an App, host.json, XML-Doku oder Abhängigkeiten überspringt der Build den Export.
- `basePath` kommt aus `extensions.http.routePrefix` in host.json (Standard `/api`), `info.version` aus der InformationalVersion ohne Anhang `+<commit>`. `host` fehlt; den setzt die Pipeline je Umgebung.
- `dotnet publish -p:SeedOpenApiPublishDir=<Ordner>` kopiert die Datei zusätzlich dorthin, etwa als Pipeline-Artefakt. Ins Deploy-Paket der Function kommt sie nicht.

Der Export lädt die gebaute App in einen eigenen Ladekontext und liest nur Attribute und Typen: Program.cs läuft nicht, App-Settings braucht es nicht. In Tests geht dasselbe direkt:

```csharp
var result = SeedOpenApiDocument.Generate(typeof(HealthFunction).Assembly);
Assert.Empty(result.Warnings);
```

| MSBuild-Eigenschaft | Standard | Bedeutung |
| --- | --- | --- |
| `SeedOpenApiEnabled` | `true` in ausführbaren Projekten | `false` schaltet den Export ab |
| `SeedOpenApiOutputPath` | `$(OutDir)openapi/openapi.json` | anderer Ort für die Datei |
| `SeedOpenApiPublishDir` | leer | Ordner, in den `dotnet publish` die Datei kopiert; nicht `PublishDir` |

Das Target liegt unter `build/`, nicht `buildTransitive/`: Es wirkt nur im Projekt mit der PackageReference, nicht in Testprojekten, die die App referenzieren.

## Warnungen und Fehler

Hinweise zeigt der Build als Warnung mit Code; openapi.json entsteht trotzdem. Wie Compiler-Warnungen erscheinen sie nur, wenn der Export läuft, also nicht bei einem Build ohne Änderung. Einzelne Codes abschalten: `<MSBuildWarningsAsMessages>$(MSBuildWarningsAsMessages);SEEDOPENAPI001</MSBuildWarningsAsMessages>`.

| Code | Bedeutung |
| --- | --- |
| `SEEDOPENAPI001` | Function ohne `[OpenApiOperation]`; operationId und summary sind der Function-Name |
| `SEEDOPENAPI002` | Function ohne `[OpenApiResponse]`; Power Automate und Copilot Studio sehen keine Ausgaben |
| `SEEDOPENAPI003` | operationId mit anderen Zeichen als Buchstaben, Ziffern und `_` |
| `SEEDOPENAPI004` | Titel länger als 30 oder summary länger als 80 Zeichen |
| `SEEDOPENAPI005` | `[HttpTrigger]` ohne Methoden (dokumentiert sind GET und POST) oder mit einer, die Swagger 2.0 nicht kennt |
| `SEEDOPENAPI006` | optionaler Routenparameter (`{id?}`, `{id=1}`), in Swagger 2.0 Pflicht, oder Catch-all (`{*pfad}`) |
| `SEEDOPENAPI007` | Body bei GET, DELETE oder HEAD; der Connector sendet ihn nicht |
| `SEEDOPENAPI008` | Parametertyp nicht abbildbar (Objekt oder Liste), dokumentiert als string |
| `SEEDOPENAPI009` | Schema nicht vollständig abbildbar: mehrere Typen, `anyOf`/`oneOf` (etwa Polymorphie, erster Zweig gilt) |
| `SEEDOPENAPI010` | zwei Typen mit gleichem Namen; der zweite bekommt eine Nummer (`Item2`) |
| `SEEDOPENAPI011` | openapi.json größer als 1 MB, der Custom Connector lehnt das ab |
| `SEEDOPENAPI012` | `AuthorizationLevel` ist nicht `Anonymous`; den Function-Key sendet der Connector nicht |

Fehler brechen den Build ab:

| Code | Bedeutung |
| --- | --- |
| `SEEDOPENAPI100` | operationId mehrfach vergeben (ohne Groß- und Kleinschreibung) |
| `SEEDOPENAPI101` | Methode und Pfad mehrfach, etwa `GET /items/{id}` in zwei Functions |
| `SEEDOPENAPI199` | Export fehlgeschlagen, etwa weil die App nicht ladbar ist oder ASP.NET Core 10 fehlt; Einzelheiten im Build-Log |

## Grenzen

- **Swagger 2.0**, weil der Custom Connector nur das annimmt. Kein `anyOf`/`oneOf`, keine Typ-Listen, keine optionalen Pfadparameter. Ein nullbares Objekt bleibt ein `$ref` ohne `x-nullable`, ist aber keine Pflicht.
- **Nur JSON** (`application/json`) für Body und Antworten. Formulare, Dateien, Header-Parameter und andere Content-Types kennt der Generator nicht. Query-Parameter gibt es nur per `[OpenApiParameter]` und nur mit einfachen Typen: string, Zahlen, bool, Guid, Datum, Enum.
- **Typen:** alles, was System.Text.Json serialisiert, also Records, Klassen, Listen, Dictionaries, Enums, Guid, `DateTime`, `DateTimeOffset`, Zahlen. Enums erscheinen als Zahlen, außer mit `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` am Typ; eigene JSON-Einstellungen in Program.cs sieht der Generator nicht.
- **Antworttypen** schreibt man selbst dazu, was ein `IActionResult` liefert, sieht der Generator nicht. Anonyme Objekte haben kein Schema, dafür Records verwenden.

## Custom Connector

Das Feature `customConnector` in [seed-template](https://github.com/blackforestsentinel/seed-template) legt aus dieser Datei je Umgebung den Custom Connector an, mit `host` und Anmeldung der Umgebung. Einrichtung und Pipeline beschreibt die README dort.
