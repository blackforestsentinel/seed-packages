using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Bfs.Seed.Functions.Core;

/// <summary>
/// Secrets aus dem Key Vault des Projekts. Das Terraform-Modul keyvault setzt je Secret das
/// App-Setting <c>Secrets__&lt;Name&gt;</c> als Key-Vault-Referenz, z. B. für <c>stripe-key</c>
/// <c>Secrets__StripeKey</c>; die Plattform ersetzt die Referenz beim Start der App durch den Wert.
/// </summary>
/// <remarks>
/// Meldungen und <see cref="Problems"/> nennen nur Setting- und Secret-Namen, nie Werte.
/// </remarks>
public sealed partial class SeedSecrets(IConfiguration configuration)
{
    /// <summary>Konfigurationsabschnitt der Secrets.</summary>
    public const string SectionName = "Secrets";

    /// <summary>Anfang des Platzhalters, den das Modul keyvault in neue Secrets schreibt; danach folgt der Secret-Name.</summary>
    public const string PlaceholderPrefix = "seed-placeholder:";

    // Kann die Plattform eine Referenz nicht auflösen, bleibt der Referenztext als Wert stehen.
    internal const string ReferencePrefix = "@Microsoft.KeyVault(";

    private readonly Lazy<IReadOnlyList<SeedSecretProblem>> problems = new(() => FindProblems(configuration));

    /// <summary>
    /// Alle App-Settings mit Platzhalter oder nicht aufgelöster Key-Vault-Referenz, auch außerhalb
    /// von <c>Secrets</c>. Die Werte stehen fest, solange der Prozess läuft: Die Plattform löst
    /// Referenzen beim Start auf.
    /// </summary>
    public IReadOnlyList<SeedSecretProblem> Problems => problems.Value;

    /// <summary>
    /// Liefert den Wert eines Secrets, z. B. <c>Get("stripe-key")</c> aus <c>Secrets:StripeKey</c>.
    /// Erst beim Gebrauch aufrufen, nicht beim Start: Bis jemand den Wert setzt, steht dort der
    /// Platzhalter, und die App soll trotzdem starten.
    /// </summary>
    /// <param name="secretName">Name des Secrets wie in project.yaml (<c>keyVault.secrets</c>).</param>
    /// <exception cref="InvalidOperationException">Das Setting fehlt, enthält den Platzhalter oder eine nicht aufgelöste Referenz.</exception>
    public string Get(string secretName)
    {
        var key = ConfigurationKey(secretName);
        var setting = AppSettingName(key);
        var value = configuration[key];

        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Secret '{secretName}' fehlt: App-Setting {setting} ist nicht gesetzt. Steht der Name unter keyVault.secrets in project.yaml?");
        }

        return Classify(value) switch
        {
            SeedSecretState.Placeholder => throw new InvalidOperationException(
                $"Secret '{secretName}' hat noch den Platzhalter (App-Setting {setting}). Wert im Key Vault setzen und die Referenzen neu laden, siehe „Secret setzen“ in der README des Projekts."),
            SeedSecretState.Unresolved => throw new InvalidOperationException(
                $"Secret '{secretName}': Die Key-Vault-Referenz im App-Setting {setting} ist nicht aufgelöst. Gibt es das Secret im Vault, hat die Identität der Function Key Vault Secrets User, und wurden die Referenzen seitdem neu geladen?"),
            _ => value,
        };
    }

    /// <summary>
    /// Konfigurationsschlüssel eines Secrets: Jedes Wort beginnt groß, die Bindestriche entfallen
    /// (<c>stripe-key</c> wird <c>Secrets:StripeKey</c>). Dieselbe Regel wie im Modul keyvault.
    /// </summary>
    /// <exception cref="ArgumentException">Der Name folgt nicht den Regeln aus project.yaml.</exception>
    public static string ConfigurationKey(string secretName)
    {
        if (secretName is null || !SecretNamePattern().IsMatch(secretName))
        {
            throw new ArgumentException(
                $"'{secretName}' ist kein gültiger Secret-Name: Kleinbuchstaben und Ziffern, Wörter durch Bindestriche getrennt, jedes Wort beginnt mit einem Buchstaben (z. B. stripe-key).",
                nameof(secretName));
        }

        var words = secretName.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return $"{SectionName}:{string.Concat(words)}";
    }

    internal static IReadOnlyList<SeedSecretProblem> FindProblems(IConfiguration configuration) =>
        configuration.AsEnumerable()
            // App Service führt jedes App-Setting zusätzlich als APPSETTING_<Name>.
            .Where(entry => !string.IsNullOrEmpty(entry.Value) && !entry.Key.StartsWith("APPSETTING_", StringComparison.OrdinalIgnoreCase))
            .Select(entry => (entry.Key, Value: entry.Value!, State: Classify(entry.Value!)))
            .Where(entry => entry.State is not null)
            .Select(entry => new SeedSecretProblem(AppSettingName(entry.Key), SecretNameOf(entry.Value), entry.State!.Value))
            .DistinctBy(problem => problem.Setting, StringComparer.OrdinalIgnoreCase)
            .OrderBy(problem => problem.Setting, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static SeedSecretState? Classify(string value) =>
        value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal) ? SeedSecretState.Placeholder
        : value.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase) ? SeedSecretState.Unresolved
        : null;

    // Nur aus Platzhalter oder Referenz, die beide keinen geheimen Wert enthalten.
    private static string? SecretNameOf(string value)
    {
        if (value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
        {
            return value[PlaceholderPrefix.Length..].Trim();
        }

        var match = ReferenceSecretNamePattern().Match(value);
        return match.Success ? match.Groups["name"].Value : null;
    }

    // Secrets:StripeKey heißt in Azure Secrets__StripeKey.
    private static string AppSettingName(string configurationKey) => configurationKey.Replace(":", "__", StringComparison.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z][a-z0-9]*)*$")]
    private static partial Regex SecretNamePattern();

    // SecretName=<name> oder SecretUri=https://<vault>.vault.azure.net/secrets/<name>[/<version>]
    [GeneratedRegex("(SecretName=|/secrets/)(?<name>[0-9A-Za-z-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReferenceSecretNamePattern();
}

/// <summary>Ein App-Setting, dessen Secret fehlt.</summary>
/// <param name="Setting">Name des App-Settings in Azure, z. B. <c>Secrets__StripeKey</c>.</param>
/// <param name="Secret">Name des Secrets im Key Vault, soweit erkennbar.</param>
/// <param name="State">Platzhalter oder nicht aufgelöste Referenz.</param>
public sealed record SeedSecretProblem(string Setting, string? Secret, SeedSecretState State);

/// <summary>Warum ein Secret nicht nutzbar ist.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SeedSecretState>))]
public enum SeedSecretState
{
    /// <summary>Im Vault steht noch der Platzhalter aus Terraform; der Wert wurde nie gesetzt.</summary>
    [JsonStringEnumMemberName("placeholder")]
    Placeholder,

    /// <summary>
    /// Die Plattform konnte die Key-Vault-Referenz nicht auflösen: Secret fehlt, Rolle fehlt oder
    /// wirkt noch nicht, oder die Referenzen wurden seitdem nicht neu geladen.
    /// </summary>
    [JsonStringEnumMemberName("unresolved")]
    Unresolved,
}
