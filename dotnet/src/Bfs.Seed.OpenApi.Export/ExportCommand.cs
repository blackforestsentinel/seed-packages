using System.Reflection;
using System.Runtime.Loader;

namespace Bfs.Seed.OpenApi.Export;

/// <summary>
/// Aufruf beim Build (build/Bfs.Seed.OpenApi.targets). Lädt die gebaute App samt Abhängigkeiten
/// (App.deps.json) in einen eigenen Ladekontext und ruft dort <c>SeedOpenApiDocument.Export</c>
/// aus der Bfs.Seed.OpenApi-Version der App auf. Kein Functions-Host, keine App-Settings, kein
/// Program.cs der App. Hinweise und Fehler gehen im kanonischen Format an MSBuild, das sie als
/// Warnung oder Fehler mit Code anzeigt. Mit <c>--readme</c> schreibt es zusätzlich die
/// Endpunkt-Tabelle in die Datei, mit <c>--check</c> prüft es sie nur (CI). <c>--quiet</c> nennt
/// Codes, die der Build als Meldung abschaltet; sie zählen nicht als Warnung.
/// </summary>
internal static class ExportCommand
{
    private const string Origin = "Bfs.Seed.OpenApi";
    private const string Library = "Bfs.Seed.OpenApi";
    private const string ExportFailed = "SEEDOPENAPI199";
    private const string Usage = "Aufruf: dotnet Bfs.Seed.OpenApi.Export.dll <App.dll> <openapi.json> [--readme <README.md> [--check]] [--quiet <Codes>]";

    /// <summary>Exit-Code: openapi.json geschrieben, aber mit Warnungen, die nicht abgeschaltet sind.</summary>
    public const int Warned = 3;

    /// <summary>
    /// 0 bei Erfolg ohne Warnung, 3 (<see cref="Warned"/>) bei Erfolg mit Warnung, 1 bei einem Fehler
    /// (immer im kanonischen Format gemeldet, darauf verlässt sich das Target), 2 bei falschem Aufruf.
    /// Das Target hält das Ergebnis nur nach 0 für aktuell; sonst läuft der Export beim nächsten Build
    /// wieder und meldet die Warnungen erneut, auch die Prüfung der Endpunkt-Tabelle.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (Parse(args) is not { } arguments)
        {
            error.WriteLine(Usage);
            return 2;
        }

        var (appArgument, outputArgument, readme, check, quiet) = arguments;
        var appPath = Path.GetFullPath(appArgument);
        var outputPath = Path.GetFullPath(outputArgument);
        if (!File.Exists(appPath))
        {
            output.WriteLine($"{Origin} : error {ExportFailed}: App-Assembly {appPath} fehlt");
            return 1;
        }

        try
        {
            var context = new AppLoadContext(appPath);
            var appAssembly = context.LoadFromAssemblyPath(appPath);
            var library = context.LoadFromAssemblyName(new AssemblyName(Library));
            // Ohne README die alte Signatur: Sie gibt es in jeder Version der Bibliothek.
            Type[] signature = readme is null
                ? [typeof(Assembly), typeof(string)]
                : [typeof(Assembly), typeof(string), typeof(string), typeof(bool), typeof(string[])];
            var export = library.GetType($"{Library}.SeedOpenApiDocument")?.GetMethod("Export", signature)
                ?? throw new InvalidOperationException($"{Library} {library.GetName().Version} in der App kennt SeedOpenApiDocument.Export({signature.Length} Parameter) nicht; Paketversion prüfen");

            object?[] parameters = readme is null ? [appAssembly, outputPath] : [appAssembly, outputPath, Path.GetFullPath(readme), check, quiet];
            var warnings = (string[])export.Invoke(null, parameters)!;
            foreach (var warning in warnings)
            {
                output.WriteLine($"{Origin} : warning {warning}");
            }

            output.WriteLine($"openapi.json: {outputPath}");
            var codes = new HashSet<string>(quiet, StringComparer.OrdinalIgnoreCase);
            return warnings.All(w => codes.Contains(w.Split(':', 2)[0])) ? 0 : Warned;
        }
        catch (Exception e)
        {
            var cause = e is TargetInvocationException { InnerException: { } inner } ? inner : e;

            // SeedOpenApiException stammt aus der App-Kopie der Bibliothek; den Code deshalb per Name lesen.
            if (cause.GetType().FullName == $"{Library}.SeedOpenApiException" && cause.GetType().GetProperty("Code")?.GetValue(cause) is string code)
            {
                output.WriteLine($"{Origin} : error {code}: {cause.Message}");
                return 1;
            }

            output.WriteLine($"{Origin} : error {ExportFailed}: openapi.json für {Path.GetFileName(appPath)} nicht erzeugt: {cause.Message}");
            error.WriteLine(cause);
            return 1;
        }
    }

    /// <summary><c>&lt;App.dll&gt; &lt;openapi.json&gt; [--readme &lt;Datei&gt; [--check]] [--quiet &lt;Codes&gt;]</c>; null bei anderem Aufruf.</summary>
    private static (string App, string Output, string? Readme, bool Check, string[] Quiet)? Parse(string[] args)
    {
        if (args.Length < 2 || args[0].StartsWith("--", StringComparison.Ordinal) || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        string? readme = null;
        string[]? quiet = null;
        var check = false;
        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--readme" when readme is null && i + 1 < args.Length && args[i + 1].Length > 0:
                    readme = args[++i];
                    break;
                case "--check" when !check:
                    check = true;
                    break;
                // Aus MSBuildWarningsAsMessages und NoWarn: getrennt mit ; , oder Leerzeichen, auch leer.
                case "--quiet" when quiet is null && i + 1 < args.Length:
                    quiet = args[++i].Split([';', ',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                default:
                    return null;
            }
        }

        return check && readme is null ? null : (args[0], args[1], readme, check, quiet ?? []);
    }

    /// <summary>
    /// Löst Assemblies wie die App selbst auf (über deren deps.json, ohne sie aus ihrem Ordner);
    /// das Shared Framework (.NET, ASP.NET Core) kommt aus dem Standardkontext dieses Werkzeugs.
    /// </summary>
    private sealed class AppLoadContext(string appPath) : AssemblyLoadContext("seed-openapi")
    {
        private readonly AssemblyDependencyResolver resolver = new(appPath);

        protected override Assembly? Load(AssemblyName assemblyName) =>
            resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
