using System.Reflection;

namespace Bfs.Seed.Functions.Core;

/// <summary>Antwort des Health-Endpunkts, die Pipeline prüft sie im Smoke-Test.</summary>
/// <param name="Status">
/// <c>ok</c>, solange die Function antwortet; <c>degraded</c>, wenn Secrets fehlen (siehe
/// <see cref="Secrets"/>). Der Endpunkt antwortet in beiden Fällen mit 200, damit der erste
/// Deploy vor dem Setzen der Werte durchläuft.
/// </param>
/// <param name="Project">Projektname.</param>
/// <param name="Environment">Umgebung.</param>
/// <param name="Version">Informational Version der App, in der Pipeline die Build-Nummer.</param>
public sealed record SeedHealthReport(string Status, string Project, string Environment, string Version)
{
    /// <summary>App-Settings mit Platzhalter oder nicht aufgelöster Key-Vault-Referenz, nur Namen.</summary>
    public IReadOnlyList<SeedSecretProblem> Secrets { get; init; } = [];

    /// <summary>Erzeugt den Report aus den Seed-Optionen und der Version der App-Assembly.</summary>
    public static SeedHealthReport Create(SeedOptions options, Assembly appAssembly) => new(
        Status: "ok",
        Project: options.Project,
        Environment: options.Environment,
        Version: appAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    /// <summary>Wie <see cref="Create(SeedOptions, Assembly)"/>, dazu die Funde aus <see cref="SeedSecrets"/>.</summary>
    public static SeedHealthReport Create(SeedOptions options, Assembly appAssembly, SeedSecrets secrets)
    {
        var report = Create(options, appAssembly);
        return secrets.Problems.Count == 0
            ? report
            : report with { Status = "degraded", Secrets = secrets.Problems };
    }
}
