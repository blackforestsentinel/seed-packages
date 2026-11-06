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
            var deprecated = !e.Deprecated ? "" : e.DeprecationMessage is null ? "ja" : $"ja: {Text(e.DeprecationMessage)}";

            // Access baut der Generator selbst, Capabilities schon als Code.
            var access = e.Access.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
            lines.Add(Row(e.Method.ToUpperInvariant(), Code(e.Path), Code(e.OperationId), Text(e.Summary), access, deprecated));
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

        if (FindMarkers(text) is not { } markers)
        {
            return new(SeedOpenApiCodes.EndpointTableMarkers, $"{fullPath}: Marker {StartMarker} und danach {EndMarker} fehlen, je auf einer eigenen Zeile außerhalb von Codeblöcken; Endpunkt-Tabelle nicht geschrieben");
        }

        var (start, end) = markers;

        // Leerzeilen um die Tabelle: So endet sie in jedem Markdown-Renderer vor dem Marker.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var block = newline + newline + table.ReplaceLineEndings(newline) + newline + newline;
        var current = text[start..end];
        if (Normalize(current) == Normalize(block))
        {
            return null;
        }

        if (check)
        {
            return new(SeedOpenApiCodes.EndpointTableOutdated, $"{fullPath}: Endpunkt-Tabelle veraltet; lokal bauen und die Datei committen");
        }

        var updated = string.Concat(text.AsSpan(0, start), block, text.AsSpan(end));
        File.WriteAllBytes(fullPath, [.. bom ? Bom : [], .. Utf8.GetBytes(updated)]);
        return null;
    }

    /// <summary>
    /// Bereich zwischen den Markern: vom Ende des Start-Markers bis zum Anfang des End-Markers, oder null.
    /// Gilt nur je auf einer eigenen Zeile außerhalb von Codeblöcken (``` oder ~~~): Eine README, die die
    /// Marker als Beispiel zeigt, bekommt die Tabelle nicht in ihr Beispiel geschrieben.
    /// </summary>
    private static (int Start, int End)? FindMarkers(string text)
    {
        int? start = null;
        char? fence = null;
        for (var lineStart = 0; lineStart <= text.Length;)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            lineEnd = lineEnd < 0 ? text.Length : lineEnd;
            var line = text[lineStart..lineEnd].Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = fence is null ? line[0] : fence == line[0] ? null : fence;
            }
            else if (fence is null && start is null && line == StartMarker)
            {
                start = text.IndexOf(StartMarker, lineStart, StringComparison.Ordinal) + StartMarker.Length;
            }
            else if (fence is null && start is not null && line == EndMarker)
            {
                return (start.Value, text.IndexOf(EndMarker, lineStart, StringComparison.Ordinal));
            }

            lineStart = lineEnd + 1;
        }

        return null;
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim();

    private static string Row(params string[] cells) => $"| {string.Join(" | ", cells)} |";

    /// <summary>
    /// Text aus summary oder Hinweis darf die Tabelle nicht verändern: Senkrechte Striche trennen Zellen,
    /// Zeilenumbrüche beenden die Zeile, &lt; beginnt HTML wie einen Kommentar oder Marker, ein Backtick
    /// einen Code-Abschnitt bis in die nächste Zelle.
    /// </summary>
    private static string Text(string text) => text.ReplaceLineEndings(" ")
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Trim();

    /// <summary>Pfad und operationId als Code; einen Backtick darin gibt es dort nicht.</summary>
    private static string Code(string text) => $"`{text.Replace("|", "\\|", StringComparison.Ordinal)}`";
}
