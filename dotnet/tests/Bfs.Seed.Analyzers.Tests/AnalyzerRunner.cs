using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bfs.Seed.Analyzers.Tests;

/// <summary>
/// Übersetzt Beispielcode gegen die Assemblies dieses Testprozesses und lässt einen Analyzer darüber
/// laufen. Ohne Microsoft.CodeAnalysis.Testing: Das lädt Referenz-Assemblies zur Laufzeit aus NuGet,
/// diese Tests laufen offline gegen die echten Attribute aus Functions Worker, Bfs.Seed.Auth und MCP.
/// </summary>
internal static class AnalyzerRunner
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> AllReferences = new(() =>
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path)),
    ]);

    /// <summary>
    /// Diagnosen des Analyzers, sortiert nach Position. Unterdrückte Diagnosen stehen mit
    /// <see cref="Diagnostic.IsSuppressed"/> dabei, damit Tests auch das prüfen können.
    /// </summary>
    /// <param name="analyzer">Der Analyzer.</param>
    /// <param name="files">Pfad und Quelltext je Datei; der Pfad zählt etwa für <c>*.g.cs</c>.</param>
    /// <param name="severities">Wie NoWarn oder .editorconfig: Schwere je ID.</param>
    /// <param name="without">Assemblies, die der Beispielcode nicht referenziert, z. B. <c>Bfs.Seed.Auth</c>.</param>
    public static async Task<IReadOnlyList<Diagnostic>> RunAsync(
        DiagnosticAnalyzer analyzer, (string Path, string Source)[] files, IReadOnlyDictionary<string, ReportDiagnostic>? severities = null, params string[] without)
    {
        var trees = files.Select(file => CSharpSyntaxTree.ParseText(
            file.Source, new CSharpParseOptions(LanguageVersion.Latest), path: file.Path, cancellationToken: TestContext.Current.CancellationToken));
        var references = AllReferences.Value
            .Where(r => !without.Contains(Path.GetFileNameWithoutExtension(r.Display), StringComparer.OrdinalIgnoreCase));
        var compilation = CSharpCompilation.Create(
            "Beispiel",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                .WithSpecificDiagnosticOptions(severities ?? new Dictionary<string, ReportDiagnostic>()));

        // Ein Fehler im Beispielcode selbst ließe einen Test aus dem falschen Grund grün werden.
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true);
        var diagnostics = await compilation
            .WithAnalyzers([analyzer], options)
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        // Wirft der Analyzer, meldet der Compiler AD0001; das soll kein Test übersehen.
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start)];
    }

    public static Task<IReadOnlyList<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source, params string[] without) =>
        RunAsync(analyzer, [("Test0.cs", source)], null, without);

    /// <summary>Wie oben mit mehreren Dateien namens <c>Test&lt;n&gt;.cs</c>.</summary>
    public static Task<IReadOnlyList<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string[] sources, IReadOnlyDictionary<string, ReportDiagnostic>? severities = null) =>
        RunAsync(analyzer, [.. sources.Select((source, i) => ($"Test{i}.cs", source))], severities);

    /// <summary>Die wirksamen, also nicht unterdrückten Diagnosen.</summary>
    public static IReadOnlyList<Diagnostic> Effective(this IReadOnlyList<Diagnostic> diagnostics) =>
        [.. diagnostics.Where(d => !d.IsSuppressed)];
}
