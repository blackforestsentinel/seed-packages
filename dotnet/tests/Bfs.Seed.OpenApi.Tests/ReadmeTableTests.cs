using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Bfs.Seed.Auth;
using Bfs.Seed.OpenApi.Export;
using Bfs.Seed.OpenApi.TestApp;

namespace Bfs.Seed.OpenApi.Tests;

/// <summary>
/// Endpunkt-Tabelle: Inhalt aus der Test-App, Schreiben zwischen den Markern, Prüfen in der CI,
/// Aufruf über Export und das Werkzeug wie beim Build.
/// </summary>
public class ReadmeTableTests
{
    private const string Start = "<!-- seed:endpoints:start -->";
    private const string End = "<!-- seed:endpoints:end -->";

    private static readonly string Table = TestDocuments.App.EndpointTable();

    [Fact]
    public void EndpointTable_ListsOperationsInDocumentOrderWithAccessAndDeprecation()
    {
        Assert.Equal(
            """
            | Methode | Pfad | operationId | Zusammenfassung | Capability | Deprecated |
            | --- | --- | --- | --- | --- | --- |
            | GET | `/api/archive/{year}` | `ListArchive` | Archiv auflisten | – |  |
            | GET | `/api/categories` | `ListCategories` | Kategorien abrufen | – |  |
            | GET | `/api/customers` | `ListCustomers` | Kunden auflisten | – |  |
            | GET | `/api/deliveries` | `ListDeliveries` | Lieferungen auflisten | `deliveries.read` |  |
            | POST | `/api/deliveries` | `CreateDelivery` | Lieferung anlegen | `deliveries.read`, `deliveries.write` |  |
            | GET | `/api/deliveries/priority/{priority}` | `ListDeliveriesByPriority` | Lieferungen einer Priorität | `deliveries.read` |  |
            | GET | `/api/health` | `GetHealth` | Status abrufen | anonym |  |
            | GET | `/api/me` | `GetMe` | Angemeldete Person abrufen | – |  |
            | GET | `/api/orders` | `ListOrders` | Bestellungen auflisten | – |  |
            | POST | `/api/orders` | `CreateOrder` | Bestellung anlegen | – |  |
            | GET | `/api/orders/count` | `CountOrders` | Bestellungen zählen | anonym |  |
            | GET | `/api/orders/{id}` | `GetOrder` | Bestellung abrufen | – |  |
            | PUT | `/api/orders/{id}` | `OrderPut` | Bestellung ersetzen oder löschen | – |  |
            | DELETE | `/api/orders/{id}` | `OrderDelete` | Bestellung ersetzen oder löschen | – |  |
            | GET | `/api/shipments` | `ListShipments` | Sendungen auflisten | `deliveries.read` | ja: Stattdessen ListDeliveries verwenden. |
            | GET | `/api/shipments/{id}` | `GetShipment` | Sendung abrufen | `deliveries.read` | ja |
            """.ReplaceLineEndings("\n"),
            Table);
    }

    [Fact]
    public void EndpointTable_EscapesTextThatWouldBreakTheTableWithoutBasePath()
    {
        // Senkrechter Strich, Zeilenumbruch, Backtick und < (HTML, also auch ein Marker) wirken nicht.
        var result = TestDocuments.For(new SeedOpenApiOptions { RoutePrefix = "" }, typeof(TableEdgeCaseFunctions));
        var table = result.EndpointTable();

        Assert.EndsWith(
            "| GET | `/piped` | `Piped` | Ein \\| im \\`Titel\\` &lt;b> | – | ja: Zeile eins Zeile \\| zwei &lt;!-- seed:endpoints:end --> |",
            table,
            StringComparison.Ordinal);

        var path = Readme($"{Start}\n{End}\nDanach.\n");
        Assert.Null(ReadmeTable.Update(path, table, check: false));
        Assert.Null(ReadmeTable.Update(path, table, check: true));
        Assert.EndsWith($"\n\n{End}\nDanach.\n", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Access_ReadsAuthAttributesByName()
    {
        // Der Generator erkennt die Attribute aus Bfs.Seed.Auth am Namen; nachgebaute Typen genügen, auch für neuere Attribute.
        var anyRole = Activator.CreateInstance(FakeAttribute("Bfs.Seed.Auth.RequireAnyRoleAttribute"))!;
        var withoutRole = Activator.CreateInstance(FakeAttribute("Bfs.Seed.Auth.AllowWithoutRoleAttribute"))!;
        var other = Activator.CreateInstance(FakeAttribute("Other.RequireAnyRoleAttribute"))!;

        Assert.Equal("jede Rolle", SeedOpenApiDocument.Access([anyRole], anonymous: false));
        Assert.Equal("ohne Rolle", SeedOpenApiDocument.Access([withoutRole], anonymous: false));
        Assert.Equal("jede Rolle", SeedOpenApiDocument.Access([withoutRole, anyRole], anonymous: false));
        Assert.Equal("–", SeedOpenApiDocument.Access([other], anonymous: false));
        Assert.Equal("`a.read`, `b.write`", SeedOpenApiDocument.Access([anyRole, new RequireCapabilityAttribute("b.write"), new RequireCapabilityAttribute("a.read"), new RequireCapabilityAttribute("b.write")], anonymous: false));
        Assert.Equal("anonym", SeedOpenApiDocument.Access([new RequireCapabilityAttribute("a.read")], anonymous: true));
    }

    [Fact]
    public void Update_WritesTableBetweenMarkersAndKeepsTheRest()
    {
        var path = Readme($"# Projekt\n\n## Endpunkte\n\n{Start}\nalt\n{End}\n\nDanach.\n");

        Assert.Null(ReadmeTable.Update(path, Table, check: false));

        Assert.Equal($"# Projekt\n\n## Endpunkte\n\n{Start}\n\n{Table}\n\n{End}\n\nDanach.\n", File.ReadAllText(path));
    }

    [Fact]
    public void Update_WithoutChange_DoesNotWrite()
    {
        var path = Readme($"{Start}\n\n{Table}\n\n{End}\n");
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        Assert.Null(ReadmeTable.Update(path, Table, check: false));
        Assert.Null(ReadmeTable.Update(path, Table, check: true));

        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Check_WarnsAndLeavesFileUntouched()
    {
        var content = $"{Start}\n| alt |\n{End}\n";
        var path = Readme(content);

        var warning = ReadmeTable.Update(path, Table, check: true);

        Assert.Equal(new SeedOpenApiWarning("SEEDOPENAPI015", $"{path}: Endpunkt-Tabelle veraltet; lokal bauen und die Datei committen"), warning);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Update_KeepsCrLfAndBomAndCheckIgnoresLineEndings()
    {
        var path = Readme($"Vorher\r\n{Start}\r\n{End}\r\n", bom: true);

        Assert.Null(ReadmeTable.Update(path, Table, check: false));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.Equal($"Vorher\r\n{Start}\r\n\r\n{Table.ReplaceLineEndings("\r\n")}\r\n\r\n{End}\r\n", text);

        // Derselbe Inhalt mit LF, etwa aus einem anderen Checkout: nicht veraltet.
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        Assert.Null(ReadmeTable.Update(path, Table, check: true));
    }

    [Fact]
    public void MarkersInCodeBlock_AreOnlyAnExample()
    {
        var example = $"Beispiel:\n\n```markdown\n{Start}\n{End}\n```\n\n~~~\n{Start}\n~~~\n\n";
        var path = Readme($"{example}## Endpunkte\n\n  {Start}\n{End}\n");

        Assert.Null(ReadmeTable.Update(path, Table, check: false));

        Assert.Equal($"{example}## Endpunkte\n\n  {Start}\n\n{Table}\n\n{End}\n", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("Ohne Marker\n")]
    [InlineData($"{Start}\nnur der Anfang\n")]
    [InlineData($"{End}\nverkehrt\n{Start}\n")]
    [InlineData($"Text {Start}\n{End}\n")]
    [InlineData($"```\n{Start}\n{End}\n```\n")]
    public void MissingMarkers_GiveWarningWithoutChange(string content)
    {
        var path = Readme(content);

        var warning = ReadmeTable.Update(path, Table, check: false);

        Assert.Equal("SEEDOPENAPI016", warning?.Code);
        Assert.Equal($"{path}: Marker {Start} und danach {End} fehlen, je auf einer eigenen Zeile außerhalb von Codeblöcken; Endpunkt-Tabelle nicht geschrieben", warning?.Message);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void MissingFile_GivesWarning()
    {
        var path = Path.Combine(Directory(), "FEHLT.md");

        var warning = ReadmeTable.Update(path, Table, check: true);

        Assert.Equal(new SeedOpenApiWarning("SEEDOPENAPI016", $"{path} fehlt; Endpunkt-Tabelle nicht geschrieben (SeedOpenApiReadme)"), warning);
    }

    [Fact]
    public void Export_WritesReadmeWithBasePathFromHostJson()
    {
        var readme = Readme($"{Start}\n{End}\n");
        var output = Path.Combine(Directory(), "openapi.json");

        // Die Test-App warnt nur mit SEEDOPENAPI006; als Meldung abgeschaltet zählt das nicht.
        var warnings = SeedOpenApiDocument.Export(typeof(OrderFunctions).Assembly, output, readme, checkReadme: false, ["SEEDOPENAPI006"]);

        Assert.Equal("SEEDOPENAPI006", Assert.Single(warnings)[..14]);
        Assert.Contains("| GET | `/v1/health` | `GetHealth` |", File.ReadAllText(readme), StringComparison.Ordinal);
    }

    [Fact]
    public void Export_WithWarnings_LeavesReadmeAloneButStillChecks()
    {
        // Was der Build bemängelt, landet nicht in der README; die CI prüft trotzdem.
        var content = $"{Start}\n{End}\n";
        var readme = Readme(content);
        var output = Path.Combine(Directory(), "openapi.json");

        var local = SeedOpenApiDocument.Export(typeof(OrderFunctions).Assembly, output, readme, checkReadme: false, []);
        Assert.Equal("SEEDOPENAPI006", Assert.Single(local)[..14]);
        Assert.Equal(content, File.ReadAllText(readme));
        Assert.True(File.Exists(output));

        var check = SeedOpenApiDocument.Export(typeof(OrderFunctions).Assembly, output, readme, checkReadme: true, []);
        Assert.Equal(["SEEDOPENAPI006", "SEEDOPENAPI015"], check.Select(w => w[..14]));
    }

    [Fact]
    public void Tool_WritesReadmeOrChecksIt()
    {
        var app = typeof(OrderFunctions).Assembly.Location;
        var readme = Readme($"{Start}\n{End}\n");
        var output = Path.Combine(Directory(), "openapi.json");

        // Exit-Code 3: geschrieben, aber mit Warnung; das Target hält das Ergebnis dann nicht für aktuell.
        var check = Run([app, output, "--readme", readme, "--check", "--quiet", "SEEDOPENAPI006"]);
        Assert.Equal(ExportCommand.Warned, check.ExitCode);
        Assert.Contains($"Bfs.Seed.OpenApi : warning SEEDOPENAPI015: {readme}: Endpunkt-Tabelle veraltet; lokal bauen und die Datei committen", check.Output);
        Assert.Equal($"{Start}\n{End}\n", File.ReadAllText(readme));

        // Ohne abgeschaltete SEEDOPENAPI006 zählt deren Warnung: README bleibt, Exit-Code 3.
        var warned = Run([app, output, "--readme", readme]);
        Assert.Equal(ExportCommand.Warned, warned.ExitCode);
        Assert.Equal($"{Start}\n{End}\n", File.ReadAllText(readme));

        var update = Run([app, output, "--readme", readme, "--quiet", "CS1591; SEEDOPENAPI006,NU1603"]);
        Assert.Equal(0, update.ExitCode);
        Assert.Contains(update.Output, l => l.StartsWith("Bfs.Seed.OpenApi : warning SEEDOPENAPI006:", StringComparison.Ordinal));
        Assert.Contains("`/v1/orders`", File.ReadAllText(readme), StringComparison.Ordinal);

        Assert.Equal(0, Run([app, output, "--readme", readme, "--check", "--quiet", "SEEDOPENAPI006"]).ExitCode);
    }

    [Theory]
    [InlineData("--check")]
    [InlineData("--readme")]
    [InlineData("--readme", "a.md", "--readme", "b.md")]
    [InlineData("--readme", "a.md", "--unbekannt")]
    [InlineData("--quiet")]
    [InlineData("--quiet", "A", "--quiet", "B")]
    public void Tool_RejectsWrongReadmeArguments(params string[] options)
    {
        var error = new StringWriter();

        Assert.Equal(2, ExportCommand.Run(["App.dll", "openapi.json", .. options], new StringWriter(), error));
        Assert.Contains("--readme", error.ToString(), StringComparison.Ordinal);
    }

    private static (int ExitCode, string[] Output) Run(string[] args)
    {
        var output = new StringWriter();
        var exitCode = ExportCommand.Run(args, output, new StringWriter());
        return (exitCode, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string Readme(string content, bool bom = false)
    {
        var path = Path.Combine(Directory(), "README.md");
        File.WriteAllText(path, content, new UTF8Encoding(bom));
        return path;
    }

    private static string Directory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "readme-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    private static Type FakeAttribute(string fullName)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Fake" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run).DefineDynamicModule("Fake");
        return module.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute)).CreateType();
    }
}
