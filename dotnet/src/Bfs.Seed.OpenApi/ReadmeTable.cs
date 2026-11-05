using System.Text;

namespace Bfs.Seed.OpenApi;

/// <summary>Eine Operation in der Endpunkt-Tabelle.</summary>
/// <param name="Method">Methode klein wie im Dokument, etwa <c>get</c>.</param>
/// <param name="DocumentPath">Pfad wie unter <c>paths</c>, zum Sortieren.</param>
/// <param name="Path">Pfad mit <c>basePath</c>, wie ihn ein Client aufruft.</param>
/// <param name="OperationId">operationId im Dokument.</param>
/// <param name="Summary">summary ohne den Zusatz <c>(deprecated)</c>; dafür gibt es eine Spalte.</param>
/// <param name="Deprecated">Veraltet.</param>
/// <param name="DeprecationMessage">Hinweis zur Ablösung oder null.</param>
/// <param name="Access">Capability-Spalte, siehe <see cref="SeedOpenApiDocument.Access"/>.</param>
internal sealed record SeedOpenApiEndpoint(
    string Method,
    string DocumentPath,
    string Path,
    string OperationId,
    string Summary,
    bool Deprecated,
    string? DeprecationMessage,
    string Access);

/// <summary>
/// Endpunkt-Tabelle in einer Markdown-Datei, meist der README des Projekts, zwischen festen Markern.
/// Lokal schreibt der Build die Tabelle, in der CI prüft er nur: Doku und Code laufen nicht auseinander,
/// und die CI ändert keine Dateien im Checkout.
/// </summary>
internal static class ReadmeTable
{
    public const string StartMarker = "<!-- seed:endpoints:start -->";
    public const string EndMarker = "<!-- seed:endpoints:end -->";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Tabelle mit LF, ohne Zeilenumbruch am Ende.</summary>
    public static string Render(IEnumerable<SeedOpenApiEndpoint> endpoints)
    {
        var lines = new List<string>
        {
            "| Methode | Pfad | operationId | Zusammenfassung | Capability | Deprecated |",
            "| --- | --- | --- | --- | --- | --- |",
        };
        foreach (var e in endpoints)
        {
            var deprecated = !e.Deprecated ? "" : e.DeprecationMessage is null ? "ja" : $"ja: {e.DeprecationMessage}";
            lines.Add(Row(e.Method.ToUpperInvariant(), Code(e.Path), Code(e.OperationId), e.Summary, e.Access, deprecated));
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Schreibt <paramref name="table"/> zwischen die Marker in <paramref name="path"/>, mit
    /// <paramref name="check"/> nur Vergleich. Zeilenende und BOM der Datei bleiben, Unterschiede
    /// nur im Zeilenende zählen nicht (Checkout mit CRLF). Gibt eine Warnung zurück oder null.
    /// </summary>
    public static SeedOpenApiWarning? Update(string path, string table, bool check)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new(SeedOpenApiCodes.EndpointTableMarkers, $"{fullPath} fehlt; Endpunkt-Tabelle nicht geschrieben (SeedOpenApiReadme)");
        }

        var bytes = File.ReadAllBytes(fullPath);
        var bom = bytes.AsSpan().StartsWith(Bom);
        var text = Utf8.GetString(bom ? bytes[Bom.Length..] : bytes);

        var start = text.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf(EndMarker, start + StartMarker.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            return new(SeedOpenApiCodes.EndpointTableMarkers, $"{fullPath}: Marker {StartMarker} und danach {EndMarker} fehlen; Endpunkt-Tabelle nicht geschrieben");
        }

        // Leerzeilen um die Tabelle: So endet sie in jedem Markdown-Renderer vor dem Marker.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var block = newline + newline + table.ReplaceLineEndings(newline) + newline + newline;
        var current = text[(start + StartMarker.Length)..end];
        if (Normalize(current) == Normalize(block))
        {
            return null;
        }

        if (check)
        {
            return new(SeedOpenApiCodes.EndpointTableOutdated, $"{fullPath}: Endpunkt-Tabelle veraltet; lokal bauen und die Datei committen");
        }

        var updated = string.Concat(text.AsSpan(0, start + StartMarker.Length), block, text.AsSpan(end));
        File.WriteAllBytes(fullPath, [.. bom ? Bom : [], .. Utf8.GetBytes(updated)]);
        return null;
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim();

    private static string Row(params string[] cells) => $"| {string.Join(" | ", cells.Select(Cell))} |";

    /// <summary>Senkrechte Striche trennen Zellen, Zeilenumbrüche beenden die Zeile; beides darf im Text nicht wirken.</summary>
    private static string Cell(string text) => text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();

    private static string Code(string text) => $"`{text}`";
}
