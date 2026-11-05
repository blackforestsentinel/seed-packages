using System.Reflection;

namespace Bfs.Seed.Functions.Core;

/// <summary>Antwort des Health-Endpunkts, die Pipeline prüft sie im Smoke-Test.</summary>
/// <param name="Status">
/// <c>ok</c>, solange die Function antwortet; <c>degraded</c>, wenn Secrets fehlen, bald ablaufen
/// oder abgelaufen sind (siehe <see cref="Secrets"/>). Der Endpunkt antwortet in beiden Fällen mit
/// 200, damit der erste Deploy vor dem Setzen der Werte durchläuft.
/// </param>
/// <param name="Project">Projektname.</param>
/// <param name="Environment">Umgebung.</param>
/// <param name="Version">Informational Version der App, in der Pipeline die Build-Nummer.</param>
public sealed record SeedHealthReport(string Status, string Project, string Environment, string Version)
{
    /// <summary>
    /// App-Settings mit Platzhalter oder nicht aufgelöster Key-Vault-Referenz, mit
    /// <see cref="CreateAsync"/> auch Secrets, die bald ablaufen; nur Namen und Ablaufdaten, nie Werte.
    /// </summary>
    public IReadOnlyList<SeedSecretProblem> Secrets { get; init; } = [];

    /// <summary>Erzeugt den Report aus den Seed-Optionen und der Version der App-Assembly.</summary>
    public static SeedHealthReport Create(SeedOptions options, Assembly appAssembly) => new(
        Status: "ok",
        Project: options.Project,
        Environment: options.Environment,
        Version: appAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    /// <summary>Wie <see cref="Create(SeedOptions, Assembly)"/>, dazu die Funde aus <see cref="SeedSecrets"/>.</summary>
    public static SeedHealthReport Create(SeedOptions options, Assembly appAssembly, SeedSecrets secrets) =>
        WithProblems(Create(options, appAssembly), secrets.Problems);

    /// <summary>
    /// Wie <see cref="Create(SeedOptions, Assembly, SeedSecrets)"/>, dazu Secrets aus
    /// <c>keyVault.secrets</c>, deren Ablaufdatum im Key Vault in den nächsten 30 Tagen liegt
    /// (<c>expiring</c>) oder überschritten ist (<c>expired</c>). Secrets ohne Ablaufdatum oder mit
    /// späterem nennt der Report nicht, weil der Endpunkt anonym ist; alle Ablaufdaten liefert
    /// <see cref="SeedSecrets.GetExpirationsAsync"/>.
    /// </summary>
    /// <remarks>
    /// Braucht <c>Seed__KeyVaultUri</c>, sonst fehlen nur die Ablaufdaten. Antwortet der Vault nicht
    /// binnen fünf Sekunden oder mit einem Fehler, meldet der Report <c>expiryUnknown</c>.
    /// </remarks>
    public static async Task<SeedHealthReport> CreateAsync(
        SeedOptions options, Assembly appAssembly, SeedSecrets secrets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var expiry = await secrets.FindExpiryProblemsAsync(cancellationToken);
        return WithProblems(Create(options, appAssembly), [.. secrets.Problems, .. expiry]);
    }

    private static SeedHealthReport WithProblems(SeedHealthReport report, IReadOnlyList<SeedSecretProblem> problems) =>
        problems.Count == 0 ? report : report with { Status = "degraded", Secrets = problems };
}
