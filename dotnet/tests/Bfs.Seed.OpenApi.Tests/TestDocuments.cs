using System.Text.Json.Nodes;
using Bfs.Seed.OpenApi.TestApp;

namespace Bfs.Seed.OpenApi.Tests;

/// <summary>Erzeugte Dokumente und Zugriffe darauf.</summary>
internal static class TestDocuments
{
    /// <summary>Dokument der Test-App, wie es der Build erzeugt (ohne host.json, also basePath /api).</summary>
    public static SeedOpenApiResult App { get; } = SeedOpenApiDocument.Generate(typeof(OrderFunctions).Assembly);

    /// <summary>Dokument nur für die angegebenen Typen dieser Test-Assembly (Sonderfälle).</summary>
    public static SeedOpenApiResult For(params Type[] types) => SeedOpenApiDocument.Generate(typeof(TestDocuments).Assembly, types, null);

    public static SeedOpenApiResult For(SeedOpenApiOptions options, params Type[] types) => SeedOpenApiDocument.Generate(typeof(TestDocuments).Assembly, types, options);

    public static IEnumerable<(string Path, string Method, JsonObject Operation)> Operations(this SeedOpenApiResult result) =>
        result.Document["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject().Select(m => (p.Key, m.Key, m.Value!.AsObject())));

    public static JsonObject Operation(this SeedOpenApiResult result, string path, string method) =>
        result.Document["paths"]![path]![method]!.AsObject();

    public static JsonObject Definition(this SeedOpenApiResult result, string name) =>
        result.Document["definitions"]![name]!.AsObject();

    public static JsonObject Properties(this SeedOpenApiResult result, string definition) =>
        result.Definition(definition)["properties"]!.AsObject();

    public static IEnumerable<string> Required(this SeedOpenApiResult result, string definition) =>
        result.Definition(definition)["required"]?.AsArray().Select(r => r!.GetValue<string>()) ?? [];

    public static JsonObject Parameter(this JsonObject operation, string name) =>
        operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!.AsObject();

    /// <summary>Wert als string, null wenn er fehlt oder kein string ist.</summary>
    public static string? Text(this JsonNode? node, string key) =>
        node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static IEnumerable<string> Codes(this SeedOpenApiResult result) => result.Warnings.Select(w => w.Code);
}
