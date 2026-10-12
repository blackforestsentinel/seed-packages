using System.Reflection;
using System.Runtime.Loader;

namespace Bfs.Seed.OpenApi.Export;

/// <summary>
/// Aufruf beim Build (build/Bfs.Seed.OpenApi.targets). Lädt die gebaute App samt Abhängigkeiten
/// (App.deps.json) in einen eigenen Ladekontext und ruft dort <c>SeedOpenApiDocument.Export</c>
/// aus der Bfs.Seed.OpenApi-Version der App auf. Kein Functions-Host, keine App-Settings, kein
/// Program.cs der App. Hinweise und Fehler gehen im kanonischen Format an MSBuild, das sie als
/// Warnung oder Fehler mit Code anzeigt.
/// </summary>
internal static class ExportCommand
{
    private const string Origin = "Bfs.Seed.OpenApi";
    private const string Library = "Bfs.Seed.OpenApi";
    private const string ExportFailed = "SEEDOPENAPI199";

    /// <summary>
    /// 0 bei Erfolg, 1 bei einem Fehler (immer im kanonischen Format gemeldet, darauf verlässt sich
    /// das Target), 2 bei falschem Aufruf.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2)
        {
            error.WriteLine("Aufruf: dotnet Bfs.Seed.OpenApi.Export.dll <App.dll> <openapi.json>");
            return 2;
        }

        var appPath = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);
        if (!File.Exists(appPath))
        {
            output.WriteLine($"{Origin} : error {ExportFailed}: App-Assembly {appPath} fehlt");
            return 1;
        }

        try
        {
            var context = new AppLoadContext(appPath);
            var app = context.LoadFromAssemblyPath(appPath);
            var library = context.LoadFromAssemblyName(new AssemblyName(Library));
            var export = library.GetType($"{Library}.SeedOpenApiDocument")?.GetMethod("Export", [typeof(Assembly), typeof(string)])
                ?? throw new InvalidOperationException($"{Library} {library.GetName().Version} in der App kennt SeedOpenApiDocument.Export nicht; Paketversion prüfen");

            var warnings = (string[])export.Invoke(null, [app, outputPath])!;
            foreach (var warning in warnings)
            {
                output.WriteLine($"{Origin} : warning {warning}");
            }

            output.WriteLine($"openapi.json: {outputPath}");
            return 0;
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
