using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bfs.Seed.OpenApi;

/// <summary>Erzeugtes Dokument und Hinweise, die der Build als Warnung ausgibt.</summary>
public sealed record SeedOpenApiResult(JsonObject Document, IReadOnlyList<SeedOpenApiWarning> Warnings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Eingerückt, Umlaute unverändert, Zeilenende LF auf jedem System, mit Zeilenumbruch am Ende.</summary>
    public string ToJson() => Document.ToJsonString(JsonOptions) + "\n";
}

/// <summary>Hinweis zum Dokument; der Build zeigt ihn als Warnung mit <see cref="Code"/>.</summary>
/// <param name="Code">Code wie <c>SEEDOPENAPI001</c>, siehe README.</param>
/// <param name="Message">Text mit dem Namen der Function oder des Typs.</param>
public sealed record SeedOpenApiWarning(string Code, string Message)
{
    /// <summary><c>Code: Text</c>, so wie das Export-Werkzeug ihn an MSBuild gibt.</summary>
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Fehler, mit dem kein brauchbares Dokument entsteht, etwa eine doppelte operationId; bricht den Build ab.</summary>
public sealed class SeedOpenApiException : Exception
{
    /// <summary>Fehler mit Code wie <c>SEEDOPENAPI100</c>.</summary>
    public SeedOpenApiException(string code, string message)
        : base(message) => Code = code;

    /// <summary>Code des Fehlers, siehe README.</summary>
    public string Code { get; }
}

/// <summary>Codes der Warnungen und Fehler; die README nennt sie mit Erklärung.</summary>
internal static class SeedOpenApiCodes
{
    public const string MissingOperation = "SEEDOPENAPI001";
    public const string MissingResponse = "SEEDOPENAPI002";
    public const string OperationIdFormat = "SEEDOPENAPI003";
    public const string TooLong = "SEEDOPENAPI004";
    public const string Methods = "SEEDOPENAPI005";
    public const string RouteParameter = "SEEDOPENAPI006";
    public const string BodyNotSent = "SEEDOPENAPI007";
    public const string ParameterType = "SEEDOPENAPI008";
    public const string SchemaNotMappable = "SEEDOPENAPI009";
    public const string DefinitionName = "SEEDOPENAPI010";
    public const string TooLarge = "SEEDOPENAPI011";
    public const string FunctionKey = "SEEDOPENAPI012";

    public const string DuplicateOperationId = "SEEDOPENAPI100";
    public const string DuplicateOperation = "SEEDOPENAPI101";
}
