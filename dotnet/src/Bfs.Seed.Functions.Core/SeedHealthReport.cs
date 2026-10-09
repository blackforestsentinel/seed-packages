using System.Reflection;

namespace Bfs.Seed.Functions.Core;

/// <summary>Antwort des Health-Endpunkts, die Pipeline prüft sie im Smoke-Test.</summary>
/// <param name="Status">Immer <c>ok</c>, solange die Function antwortet.</param>
/// <param name="Project">Projektname.</param>
/// <param name="Environment">Umgebung.</param>
/// <param name="Version">Informational Version der App, in der Pipeline die Build-Nummer.</param>
public sealed record SeedHealthReport(string Status, string Project, string Environment, string Version)
{
    /// <summary>Erzeugt den Report aus den Seed-Optionen und der Version der App-Assembly.</summary>
    public static SeedHealthReport Create(SeedOptions options, Assembly appAssembly) => new(
        Status: "ok",
        Project: options.Project,
        Environment: options.Environment,
        Version: appAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");
}
