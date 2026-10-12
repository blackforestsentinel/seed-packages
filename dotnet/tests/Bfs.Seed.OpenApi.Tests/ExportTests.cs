using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bfs.Seed.OpenApi.Export;
using Bfs.Seed.OpenApi.TestApp;

namespace Bfs.Seed.OpenApi.Tests;

/// <summary>
/// Export wie beim Build: <see cref="SeedOpenApiDocument.Export"/> und das Werkzeug, das die App in
/// einen eigenen Ladekontext lädt. host.json im Ausgabeordner setzt den Routen-Präfix v1.
/// </summary>
public partial class ExportTests
{
    private static readonly string AppPath = typeof(OrderFunctions).Assembly.Location;

    [Fact]
    public void Export_WritesFileWithRoutePrefixFromHostJson()
    {
        var path = OutputPath();

        var warnings = SeedOpenApiDocument.Export(typeof(OrderFunctions).Assembly, path);

        Assert.Equal("SEEDOPENAPI006", Assert.Single(warnings)[..14]);
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("/v1", document.Text("basePath"));
        Assert.Empty(SwaggerValidator.Validate(document));
        Assert.DoesNotContain("\r", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_ExportsFromOwnLoadContextAndReportsWarningsForMsBuild()
    {
        var path = OutputPath();
        var (exitCode, output, _) = Run(AppPath, path);

        Assert.Equal(0, exitCode);
        var warning = Assert.Single(output, l => l.Contains(": warning ", StringComparison.Ordinal));
        Assert.Matches(CanonicalMessage(), warning);
        Assert.StartsWith("Bfs.Seed.OpenApi : warning SEEDOPENAPI006: ListArchive:", warning, StringComparison.Ordinal);
        Assert.Equal(TestDocuments.App.Operations().Count(), JsonNode.Parse(File.ReadAllText(path))!["paths"]!.AsObject().Sum(p => p.Value!.AsObject().Count));
    }

    [Fact]
    public void Tool_ReportsDuplicateOperationIdAsErrorWithCode()
    {
        var path = OutputPath();
        var (exitCode, output, _) = Run(typeof(ExportTests).Assembly.Location, path);

        Assert.Equal(1, exitCode);
        var error = Assert.Single(output);
        Assert.Matches(CanonicalMessage(), error);
        Assert.StartsWith("Bfs.Seed.OpenApi : error SEEDOPENAPI10", error, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Tool_ReportsMissingApp()
    {
        var app = Path.Combine(AppContext.BaseDirectory, "Fehlt.dll");
        var (exitCode, output, _) = Run(app, OutputPath());

        Assert.Equal(1, exitCode);
        Assert.Equal($"Bfs.Seed.OpenApi : error SEEDOPENAPI199: App-Assembly {app} fehlt", Assert.Single(output));
    }

    [Fact]
    public void Tool_ReportsOtherFailuresWithStackTraceOnStandardError()
    {
        // host.json ist keine Assembly.
        var (exitCode, output, error) = Run(Path.Combine(AppContext.BaseDirectory, "host.json"), OutputPath());

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Bfs.Seed.OpenApi : error SEEDOPENAPI199: openapi.json für host.json nicht erzeugt:", Assert.Single(output), StringComparison.Ordinal);
        Assert.Contains("Exception", error, StringComparison.Ordinal);
        Assert.True(error.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 1, error);
    }

    [Fact]
    public void Tool_RejectsWrongArguments()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(2, ExportCommand.Run(["nur-eins"], output, error));
        Assert.StartsWith("Aufruf:", error.ToString(), StringComparison.Ordinal);
    }

    private static (int ExitCode, string[] Output, string Error) Run(string app, string path)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = ExportCommand.Run([app, path], output, error);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("openapi.json:", StringComparison.Ordinal))
            .ToArray();
        return (exitCode, lines, error.ToString());
    }

    private static string OutputPath() => Path.Combine(AppContext.BaseDirectory, "export-tests", Guid.NewGuid().ToString("N"), "openapi.json");

    /// <summary>Kanonisches Format, an dem MSBuild (Exec) Warnungen und Fehler erkennt.</summary>
    [GeneratedRegex(@"^[^:]+ : (warning|error) SEEDOPENAPI\d{3}: \S.*$")]
    private static partial Regex CanonicalMessage();
}
