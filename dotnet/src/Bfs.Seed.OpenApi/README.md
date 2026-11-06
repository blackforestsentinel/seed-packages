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
| `[OpenApiOperation(operationId, summary)]` | operationId (interner Name der Aktion), summary (Titel, höchstens 80 Zeichen), `Description` (danach wählt Copilot Studio Aktionen aus), `Visibility` (`x-ms-visibility`), `Deprecated` und `DeprecationMessage`, siehe [Operationen ablösen](#operationen-ablösen) |
| `[OpenApiParameter(name)]` | ergänzt einen Routenparameter (`Description`, `Summary` als `x-ms-summary`, `Visibility`, `Type`) oder legt einen Query-Parameter an (`Type`, Standard string, `Required`); Enums siehe [Enums](#enums) |
| `[OpenApiDynamicValues(parameter, operationId)]` | Auswahlliste für einen Parameter aus einer anderen Operation (`x-ms-dynamic-values`), siehe [Auswahllisten](#auswahllisten); an der Eigenschaft eines Body-Typs ohne `parameter` |
| `[OpenApiRequestBody(typeof(T))]` | JSON-Body, `Required` Standard ja |
| `[OpenApiResponse(status)]`, `[OpenApiResponse(status, typeof(T))]` | Antworten, mehrere erlaubt; ohne `Description` der Standardtext des Statuscodes |
| `[OpenApiIgnore]` | nimmt eine Function oder alle einer Klasse heraus |

Was der Generator selbst ermittelt:

- **Pfad und Methoden** aus `[Function]` und `[HttpTrigger]`. Routenparameter auch ohne Attribut, Typ aus dem gleichnamigen Methodenparameter oder dem Constraint (`{id:int}`, `{id:guid}`), immer mit `x-ms-url-encoding: single`. Nimmt eine Function mehrere Methoden an, gibt es je Methode eine Operation (`ItemGet`, `ItemPut`).
- **Anmeldung:** Jede Operation ohne `[AllowAnonymous]` an Methode oder Klasse bekommt `security` mit OAuth 2.0 (Entra ID, Authorization Code). Die URLs sind mandantenneutral (`login.microsoftonline.com/common`); Client-ID, Scope und Tenant setzt der Custom Connector je Umgebung.
- **Nur HTTP-Functions der App-Assembly:** Der MCP-Endpunkt aus `Bfs.Seed.Mcp`, Queue- und Timer-Functions fehlen.
- **Schemas** nach System.Text.Json mit den Web-Standards (camelCase, Zahlen als Zahlen). Objekte stehen unter `definitions` und werden per `$ref` eingebunden, auch rekursive; generische Typen heißen etwa `PageOfInvoice`.
- **Enums** mit Auswahlliste, in Pfad und Query als Namen, im JSON wie System.Text.Json; siehe [Enums](#enums).
- **Pflichtfelder** sind Konstruktorparameter ohne Standardwert, die nicht nullbar sind, sowie Eigenschaften mit `required` oder `[JsonRequired]`. Nullbare Werte (`string?`, `int?`) sind keine Pflicht und tragen `x-nullable: true`.
- **Beschreibungen** von Eigenschaften: `[Description]` an der Eigenschaft oder am Record-Parameter (`[property: Description("…")]`), sonst die XML-Doku (`<param>` des Records oder primären Konstruktors, `<summary>` der Eigenschaft), wenn das Projekt `<GenerateDocumentationFile>true</GenerateDocumentationFile>` setzt. Die `<summary>` eines Typs bleibt draußen, sie ist meist für Entwickler geschrieben. Gegen die Compiler-Warnungen zu fehlenden Kommentaren: `<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>`.

## Auswahllisten

Statt eines freien Felds für eine ID zeigen Power Automate und Copilot Studio eine Auswahlliste, deren Werte eine andere Operation liefert (`x-ms-dynamic-values`). Das Attribut steht an der Function für einen Pfad- oder Query-Parameter oder an der Eigenschaft eines Body-Typs:

```csharp
// Quelle: Prozesse, die die angemeldete Person beschreiben darf. Internal: keine eigene Aktion.
[Function("ListWritableProcesses")]
[OpenApiOperation("ListWritableProcesses", "Beschreibbare Prozesse", Visibility = OpenApiVisibility.Internal)]
[OpenApiParameter("area")]
[OpenApiResponse(200, typeof(Page<ProcessInfo>))]
public IActionResult ListWritable(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "processes/writable")] HttpRequest request) => ...;

[Function("ListTasks")]
[OpenApiOperation("ListTasks", "Aufgaben eines Prozesses")]
[OpenApiParameter("processId", Required = true, Summary = "Prozess")]
[OpenApiDynamicValues("processId", "ListWritableProcesses", ValueCollection = "items", ValuePath = "id", ValueTitle = "title", Parameters = ["area=sales"])]
[OpenApiResponse(200, typeof(IReadOnlyList<TaskItem>))]
public IActionResult ListTasks(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks")] HttpRequest request) => ...;

// Feld im Body: an der Eigenschaft, bei Records mit property:.
public sealed record CreateTaskRequest(
    [property: OpenApiDynamicValues("ListWritableProcesses", ValueCollection = "items", ValuePath = "id", ValueTitle = "title")] string ProcessId,
    string Title);

public sealed record Page<T>(IReadOnlyList<T> Items, string? NextLink);

public sealed record ProcessInfo(string Id, string Title);
```

Am Parameter `processId` steht dann (an der Eigenschaft `processId` unter `definitions` genauso, mit leerem `parameters`):

```json
"x-ms-dynamic-values": {
  "operationId": "ListWritableProcesses",
  "value-path": "id",
  "value-title": "title",
  "value-collection": "items",
  "parameters": { "area": "sales" }
}
```

| Eigenschaft | Schlüssel | Bedeutung |
| --- | --- | --- |
| `OperationId` | `operationId` | Quelle der Werte; nimmt ihre Function mehrere Methoden an, mit angehängter Methode (`ItemGet`) |
| `ValueCollection` | `value-collection` | Pfad zur Liste in der Antwort; ohne Angabe ist die Antwort selbst die Liste |
| `ValuePath` | `value-path` | Pfad zum Wert in einem Element; er landet im Parameter |
| `ValueTitle` | `value-title` | Pfad zum angezeigten Text in einem Element |
| `Parameters` | `parameters` | Aufruf der Quelle: `name=wert` mit festem Wert, `name={anderer}` mit dem Wert des Parameters `anderer` der Operation, in der die Auswahl steht (`{ "parameter": "anderer" }`) |

Pfade sind mit `/` getrennt, ohne führenden `/` (`data/items`), Namen wie im JSON (camelCase). Feste Werte stehen als Text im Dokument. `parameters` fehlt nie, Microsoft verlangt es auch leer.

Der Generator prüft beim Erzeugen: Die operationId muss es im Dokument geben (`SEEDOPENAPI102`, mit Vorschlag bei angehängter Methode oder anderer Schreibweise), das Attribut muss zu einem Parameter passen (`SEEDOPENAPI103`). Als Warnung `SEEDOPENAPI013`: Parameter, die die Quelle nicht kennt oder die ihr als Pflicht fehlen, Verweise auf Parameter, die die Operation nicht hat, und Pfade, die im Antwortschema der Quelle fehlen.

- **Quelle mit `Visibility = OpenApiVisibility.Internal`:** Sie erscheint nicht als eigene Aktion, liefert aber die Werte.
- **Keine Berechtigungsprüfung:** Die Quelle läuft mit der Anmeldung der Person und filtert selbst; die Aktion prüft den Wert trotzdem, Aufrufe ohne die Auswahlliste sind jederzeit möglich.
- **Grenzen:** Verweise nur auf ganze Parameter, nicht auf Felder im Body; dafür bräuchte es `x-ms-dynamic-list`, das der Generator nicht schreibt. Gedacht für einfache Werte (Text, Zahl, Guid); an einer Eigenschaft mit Objekttyp schreibt der Generator keine Auswahlliste. Verweise an Eigenschaften prüft der Generator nicht, sie gelten für jede Operation, die den Typ nutzt.

## Operationen ablösen

Flows rufen Aktionen über ihre operationId auf. Wer eine Operation ersetzt, markiert die alte zuerst als veraltet, lässt die Flows umstellen und entfernt sie erst danach. Zwei Wege mit demselben Ergebnis:

```csharp
// Hinweis aus [Obsolete] an der Methode
[Obsolete("Stattdessen GetInvoiceV2 verwenden.")]
[Function("GetInvoice")]
[OpenApiOperation("GetInvoice", "Rechnung abrufen", Description = "Rechnung mit Positionen.")]

// am Attribut, ohne Compiler-Warnung in Tests, die die Methode aufrufen
[Function("GetInvoice")]
[OpenApiOperation("GetInvoice", "Rechnung abrufen", Description = "Rechnung mit Positionen.",
    Deprecated = true, DeprecationMessage = "Stattdessen GetInvoiceV2 verwenden.")]
```

Im Dokument steht dann:

```json
"summary": "Rechnung abrufen (deprecated)",
"description": "(deprecated) Stattdessen GetInvoiceV2 verwenden. Rechnung mit Positionen.",
"operationId": "GetInvoice",
"deprecated": true
```

- `deprecated: true` an der Operation. Den Zusatz `(deprecated)` an summary und description empfiehlt Microsoft; der Generator setzt ihn, wenn er fehlt. Würde die summary damit länger als 80 Zeichen, steht er nur in der description. Der Hinweis steht vorn in der description, weil Tooltips und Copilot Studio oft nur den Anfang zeigen.
- `DeprecationMessage` allein genügt, `Deprecated = true` braucht es dann nicht. Der Hinweis am Attribut geht dem Text aus `[Obsolete]` vor. `Deprecated = false` gilt ausdrücklich, auch gegen `DeprecationMessage` und `[Obsolete]`.
- `[Obsolete]` nur an der Methode. An der Klasse wertet der Generator es nicht aus: Der Code, den das Functions-SDK für den Aufruf erzeugt, bekäme dafür CS0618, und der Build bräche mit `TreatWarningsAsErrors` ab. An der Methode unterdrückt das SDK die Warnung. Tests, die die Methode direkt aufrufen, warnen dagegen (CS0618); dann lieber `Deprecated` am Attribut. Nicht mit `[Obsolete(…, error: true)]`, diesen Fehler unterdrückt auch das SDK nicht.
- In der [Endpunkt-Tabelle](#endpunkt-tabelle-in-der-readme) steht `ja` oder `ja: <Hinweis>` in der Spalte Deprecated.

**Verhalten der Power Platform** laut Microsoft ([Deprecate an operation](https://learn.microsoft.com/en-us/connectors/custom-connectors/deprecate-operation), Stand März 2026): Der Designer blendet veraltete Operationen aus, neue Flows können sie nicht mehr wählen. Flows und Apps, die sie schon nutzen, laufen unverändert weiter. Eine Kennzeichnung in bestehenden Flows beschreibt Microsoft nicht; sichtbar ist dort der Zusatz in summary und description.

## Enums

Damit Power Automate und Copilot Studio eine Auswahl zeigen statt eines freien Felds, steht zu einem Enum die Liste der Werte im Dokument:

| Wo | Ergebnis |
| --- | --- |
| Pfad und Query | `type: string`, `enum` mit den Namen im Code; unabhängig vom Converter, auch für fremde Enums (etwa `DayOfWeek`) und nullbare Enums |
| JSON ohne Converter | `type: integer`, `enum` mit den Zahlen, `x-ms-enum-values` mit den Namen als Anzeigetext |
| JSON mit `JsonStringEnumConverter` | `type: string`, `enum` mit den Namen wie im JSON, auch mit `[JsonStringEnumMemberName]` |

- **Pfad und Query** sind Text, kein JSON. Die Function liest den Wert etwa mit `Enum.TryParse`, und das kennt nur die Namen im Code. `[JsonStringEnumMemberName]` gilt deshalb dort nicht: Aus `[JsonStringEnumMemberName("in-transit")] InTransit` wird in der Query `InTransit`. Wer dort andere Texte erwartet, liest sie selbst und deklariert den Parameter als string.
- **Listen in der Query** (`Type = typeof(IReadOnlyList<State>)`) bleiben wie bisher `type: string` ohne Auswahl, mit `SEEDOPENAPI008`. Flows setzen dort heute Text, etwa `Open,Shipped`; eine Auswahl mit einem Wert schlösse das aus. `type: array` mit `collectionFormat: csv` nimmt die Power Platform zwar an (zertifizierte Connectoren nutzen es), für bestehende Flows wäre es aber ein Typwechsel.
- **JSON ohne Converter:** Die Function erwartet Zahlen. Die Auswahl zeigt die Namen und sendet die Zahl:

  ```json
  "priority": {
    "type": "integer",
    "enum": [0, 5, 10],
    "x-ms-enum-values": [
      { "value": 0, "displayName": "Low" },
      { "value": 5, "displayName": "Normal" },
      { "value": 10, "displayName": "High" }
    ]
  }
  ```

  Das Format folgt Microsoft: je Wert ein Objekt mit `value` aus `enum` und `displayName`, jede Zahl nur einmal (Schema `x-ms-enum-values` in [microsoft/PowerPlatformConnectors](https://github.com/microsoft/PowerPlatformConnectors/blob/dev/schemas/apiDefinition.swagger.schema.json), Regeln MissingEnumValuesDisplayName, InvalidEnumValuesExtensionValue und DuplicateEnumValuesInExtension im [Swagger Validator](https://learn.microsoft.com/en-us/connectors/custom-connectors/certification-swagger-validator-rules)). Haben mehrere Namen dieselbe Zahl, gilt der erste. Wie Power Automate die Anzeigetexte in einem echten Connector darstellt, ist nicht geprüft.
- **`[Flags]`:** keine Werteliste und keine Warnung, in Pfad, Query und JSON. Kombinationen wie `Read | Write` (3) sind gültig; eine Liste der Einzelwerte schlösse sie aus. Der Typ bleibt wie bei System.Text.Json (`integer`, mit `JsonStringEnumConverter` `string`). Die Werte gehören dann in die `Description`.
- **Ohne Werteliste** warnt der Build mit `SEEDOPENAPI014`: leeres Enum oder eigener `JsonConverter` am Enum. Nur für Parameter und den Anfrage-Body, auch in verschachtelten Typen; Enums nur in Antworten warnen nicht, ihre Werte sendet der Connector nie.

## Endpunkt-Tabelle in der README

Der Build schreibt eine Tabelle der Endpunkte in eine Markdown-Datei, meist die README des Projekts. So laufen Doku und Code nicht auseinander. Dazu in der Projektdatei der Function-App, relativ zu ihr:

```xml
<PropertyGroup>
  <SeedOpenApiReadme>..\..\README.md</SeedOpenApiReadme>
</PropertyGroup>
```

und in der Datei zwei Marker an der Stelle der Tabelle, je auf einer eigenen Zeile:

```markdown
## Endpunkte

<!-- seed:endpoints:start -->
<!-- seed:endpoints:end -->
```

Nach dem Build steht dazwischen etwa:

| Methode | Pfad | operationId | Zusammenfassung | Capability | Deprecated |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/health` | `GetHealth` | Status abrufen | anonym |  |
| GET | `/api/invoices/{number}` | `GetInvoice` | Rechnung abrufen | `invoices.read` | ja: Stattdessen GetInvoiceV2 verwenden. |

- **Lokal** schreibt der Build die Tabelle zwischen die Marker, nur wenn sie sich ändert, und meldet `… (Endpunkt-Tabelle aktualisiert)`. Die Datei gehört in denselben Commit wie der Code.
- **Nur ohne Warnung:** Warnt der Export, bleibt die Datei, und der Build meldet `… nicht geschrieben, solange der Export warnt`. So landet keine halb fertige Function in der README. Codes, die `MSBuildWarningsAsMessages` oder `NoWarn` abschalten, zählen nicht.
- **In der CI** prüft er nur und ändert nichts: wenn `TF_BUILD` gesetzt ist (Azure DevOps) oder `ContinuousIntegrationBuild=true`. Weicht die Tabelle ab, warnt er mit `SEEDOPENAPI015`; dann lokal bauen und die Datei committen. Unterschiede nur im Zeilenende zählen nicht. Die Warnung kommt bei jedem Build wieder, auch beim zweiten Lauf ohne clean.
- Fehlt die Datei oder ein Marker, warnt der Build mit `SEEDOPENAPI016` und schreibt nichts. Marker in Codeblöcken zählen nicht; so darf die README sie auch als Beispiel zeigen. Ohne `SeedOpenApiReadme` passiert nichts.
- **Spalten:** Pfad mit `basePath`, Zusammenfassung ohne den Zusatz `(deprecated)`. In summary und Hinweis sind `|`, `` ` `` und `<` maskiert, damit sie Tabelle und Marker nicht stören. Capability: was `[RequireCapability]` an Methode und Klasse verlangt (alle nötig), `jede Rolle` bei `[RequireAnyRole]`, `ohne Rolle` bei `[AllowWithoutRole]` (angemeldet, auch ohne App-Rolle), `anonym` bei `[AllowAnonymous]`, sonst `–`. Das strengere Attribut gilt. Die Attribute aus Bfs.Seed.Auth erkennt der Generator am Namen; eine Abhängigkeit auf das Paket gibt es nicht.
- In Tests liefert `SeedOpenApiResult.EndpointTable()` dieselbe Tabelle.

## Wo die Datei landet

- Nach jedem Build unter `bin/<Konfiguration>/net10.0/openapi/openapi.json`. Ohne Änderung an App, Projektdatei, host.json, XML-Doku, Abhängigkeiten oder der Datei aus `SeedOpenApiReadme` überspringt der Build den Export, aber nur, wenn der letzte Export ohne Warnung lief.
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
| `SeedOpenApiReadme` | leer | Markdown-Datei für die [Endpunkt-Tabelle](#endpunkt-tabelle-in-der-readme), relativ zum Projekt |

Das Target liegt unter `build/`, nicht `buildTransitive/`: Es wirkt nur im Projekt mit der PackageReference, nicht in Testprojekten, die die App referenzieren.

## Warnungen und Fehler

Hinweise zeigt der Build als Warnung mit Code; openapi.json entsteht trotzdem. Solange der Export warnt, läuft er bei jedem Build wieder und meldet die Warnungen erneut; ein Build ohne Änderung verliert sie also nicht. Einzelne Codes abschalten: `<MSBuildWarningsAsMessages>$(MSBuildWarningsAsMessages);SEEDOPENAPI001</MSBuildWarningsAsMessages>` oder `<NoWarn>$(NoWarn);SEEDOPENAPI001</NoWarn>`; abgeschaltete Codes zählen für den Export nicht als Warnung.

Es sind Warnungen von MSBuild, nicht vom Compiler: `TreatWarningsAsErrors` macht sie nicht zu Fehlern. Das tun `dotnet build -warnaserror` (auch einzeln: `-warnaserror:SEEDOPENAPI015`) oder die Eigenschaft `MSBuildTreatWarningsAsErrors=true`.

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
| `SEEDOPENAPI013` | Auswahlliste passt nicht zur Quelle: unbekannter oder fehlender Pflichtparameter, Verweis ins Leere, Pfad nicht im Antwortschema |
| `SEEDOPENAPI014` | Enum ohne Werteliste in einem Parameter oder im Anfrage-Body: leeres Enum, eigener `JsonConverter`; der Connector zeigt keine Auswahl. Nicht für `[Flags]` |
| `SEEDOPENAPI015` | Endpunkt-Tabelle in der Datei aus `SeedOpenApiReadme` veraltet (nur in der CI); lokal bauen und die Datei committen |
| `SEEDOPENAPI016` | Datei aus `SeedOpenApiReadme` fehlt oder hat die Marker nicht; keine Endpunkt-Tabelle |

Fehler brechen den Build ab:

| Code | Bedeutung |
| --- | --- |
| `SEEDOPENAPI100` | operationId mehrfach vergeben (ohne Groß- und Kleinschreibung) |
| `SEEDOPENAPI101` | Methode und Pfad mehrfach, etwa `GET /items/{id}` in zwei Functions |
| `SEEDOPENAPI102` | `[OpenApiDynamicValues]` nennt eine operationId, die es im Dokument nicht gibt |
| `SEEDOPENAPI103` | `[OpenApiDynamicValues]` falsch verwendet: an der Function ohne oder mit unbekanntem Parameter, an einer Eigenschaft mit Parameter oder mehrfach, `Parameters` nicht in der Form `name=wert` |
| `SEEDOPENAPI199` | Export fehlgeschlagen, etwa weil die App nicht ladbar ist oder ASP.NET Core 10 fehlt; Einzelheiten im Build-Log |

## Grenzen

- **Swagger 2.0**, weil der Custom Connector nur das annimmt. Kein `anyOf`/`oneOf`, keine Typ-Listen, keine optionalen Pfadparameter. Ein nullbares Objekt bleibt ein `$ref` ohne `x-nullable`, ist aber keine Pflicht.
- **Nur JSON** (`application/json`) für Body und Antworten. Formulare, Dateien, Header-Parameter und andere Content-Types kennt der Generator nicht. Query-Parameter gibt es nur per `[OpenApiParameter]` und nur mit einfachen Typen: string, Zahlen, bool, Guid, Datum, Enum. Listen dokumentiert er als string (`SEEDOPENAPI008`).
- **Typen:** alles, was System.Text.Json serialisiert, also Records, Klassen, Listen, Dictionaries, Enums, Guid, `DateTime`, `DateTimeOffset`, Zahlen. Enums stehen im JSON als Zahlen mit Anzeigetexten, mit `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` am Typ als Namen ([Enums](#enums)). Eigene JSON-Einstellungen in Program.cs, etwa ein globaler `JsonStringEnumConverter`, sieht der Generator nicht.
- **Antworttypen** schreibt man selbst dazu, was ein `IActionResult` liefert, sieht der Generator nicht. Anonyme Objekte haben kein Schema, dafür Records verwenden.

## Custom Connector

Das Feature `customConnector` in [seed-template](https://github.com/blackforestsentinel/seed-template) legt aus dieser Datei je Umgebung den Custom Connector an, mit `host` und Anmeldung der Umgebung. Einrichtung und Pipeline beschreibt die README dort.
