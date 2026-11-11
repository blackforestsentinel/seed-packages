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
public sealed partial class SeedSecrets
{
    /// <summary>Konfigurationsabschnitt der Secrets.</summary>
    public const string SectionName = "Secrets";

    /// <summary>Anfang des Platzhalters, den das Modul keyvault in neue Secrets schreibt; danach folgt der Secret-Name.</summary>
    public const string PlaceholderPrefix = "seed-placeholder:";

    /// <summary>
    /// Adresse des Key Vaults, z. B. <c>https://kv-name.vault.azure.net/</c>, als App-Setting
    /// <c>Seed__KeyVaultUri</c>. Nur damit liest <see cref="GetExpirationsAsync"/> Ablaufdaten:
    /// Nach dem Start stehen in den App-Settings die Werte, nicht mehr die Referenzen auf den Vault.
    /// </summary>
    public const string KeyVaultUriKey = "Seed:KeyVaultUri";

    /// <summary>
    /// So lange vor Ablauf meldet der Health-Report ein Secret, wie das Key-Vault-Ereignis
    /// <c>SecretNearExpiry</c>.
    /// </summary>
    public static readonly TimeSpan ExpiryWarningPeriod = TimeSpan.FromDays(30);

    // So heißt KeyVaultUriKey in Azure; der Health-Report nennt es bei expiryUnknown.
    internal const string KeyVaultUriSetting = "Seed__KeyVaultUri";

    // Kann die Plattform eine Referenz nicht auflösen, bleibt der Referenztext als Wert stehen.
    internal const string ReferencePrefix = "@Microsoft.KeyVault(";

    // So lange wartet der Health-Report auf den Vault; danach meldet er expiryUnknown, der Aufruf
    // läuft im Hintergrund weiter und füllt den Zwischenspeicher für den nächsten.
    internal static readonly TimeSpan DefaultHealthWait = TimeSpan.FromSeconds(5);

    private readonly IConfiguration configuration;
    private readonly SeedSecretExpiryCache? expiry;
    private readonly TimeProvider time;
    private readonly TimeSpan healthWait;
    private readonly Lazy<IReadOnlyList<SeedSecretProblem>> problems;

    /// <summary>Secrets aus der Konfiguration, ohne Ablaufdaten aus dem Vault.</summary>
    public SeedSecrets(IConfiguration configuration)
        : this(configuration, expiry: null, TimeProvider.System)
    {
    }

    internal SeedSecrets(IConfiguration configuration, SeedSecretExpiryCache? expiry, TimeProvider time, TimeSpan? healthWait = null)
    {
        this.configuration = configuration;
        this.expiry = expiry;
        this.time = time;
        this.healthWait = healthWait ?? DefaultHealthWait;
        problems = new(() => FindProblems(configuration));
    }

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
    /// Ablaufdaten der Secrets aus <c>keyVault.secrets</c>, soweit im Key Vault eines gesetzt ist,
    /// nach Datum sortiert. Gezählt werden aktivierte Secrets, zu denen es ein App-Setting
    /// <c>Secrets__&lt;Name&gt;</c> gibt. Ohne <see cref="KeyVaultUriKey"/> ist die Liste leer.
    /// Seed erzwingt kein Ablaufdatum.
    /// </summary>
    /// <remarks>
    /// Ein Aufruf liest höchstens einmal je Stunde aus dem Vault, sonst aus dem Zwischenspeicher.
    /// Die Daten sind für Angemeldete gedacht, etwa hinter <c>[RequireCapability]</c>; der anonyme
    /// Health-Report nennt nur Secrets, die bald ablaufen (<see cref="SeedHealthReport.CreateAsync"/>).
    /// </remarks>
    /// <exception cref="InvalidOperationException">Der Vault ließ sich nicht lesen; Einzelheiten in der inneren Exception.</exception>
    public async Task<IReadOnlyList<SeedSecretExpiration>> GetExpirationsAsync(CancellationToken cancellationToken = default)
    {
        if (expiry is null)
        {
            return [];
        }

        var snapshot = await expiry.GetAsync().WaitAsync(cancellationToken);
        return snapshot.Error is null
            ? Managed(snapshot.Secrets)
            : throw new InvalidOperationException($"Die Ablaufdaten der Secrets ließen sich nicht aus dem Key Vault ({KeyVaultUriSetting}) lesen.", snapshot.Error);
    }

    /// <summary>
    /// Funde für den Health-Report: abgelaufene und in <see cref="ExpiryWarningPeriod"/> ablaufende
    /// Secrets, oder <see cref="SeedSecretState.ExpiryUnknown"/>, wenn der Vault nicht antwortet.
    /// </summary>
    internal async Task<IReadOnlyList<SeedSecretProblem>> FindExpiryProblemsAsync(CancellationToken cancellationToken)
    {
        if (expiry is null)
        {
            return [];
        }

        SeedSecretExpirySnapshot snapshot;
        try
        {
            snapshot = await expiry.GetAsync().WaitAsync(healthWait, time, cancellationToken);
        }
        catch (TimeoutException)
        {
            return [new SeedSecretProblem(KeyVaultUriSetting, null, SeedSecretState.ExpiryUnknown)];
        }

        if (snapshot.Error is not null)
        {
            return [new SeedSecretProblem(KeyVaultUriSetting, null, SeedSecretState.ExpiryUnknown)];
        }

        var now = time.GetUtcNow();
        return Managed(snapshot.Secrets)
            .Where(e => e.ExpiresOn <= now + ExpiryWarningPeriod)
            .Select(e => new SeedSecretProblem(e.Setting, e.Secret, e.ExpiresOn <= now ? SeedSecretState.Expired : SeedSecretState.Expiring)
            {
                ExpiresOn = e.ExpiresOn,
            })
            .ToList();
    }

    // Nur Secrets aus keyVault.secrets: Für jedes setzt das Modul keyvault Secrets__<Name>. Ein
    // deaktiviertes Secret ist ohnehin nicht nutzbar (Referenz unresolved), sein Ablauf zählt nicht.
    private List<SeedSecretExpiration> Managed(IEnumerable<SeedVaultSecret> secrets) =>
        secrets
            .Where(s => s.Enabled && s.ExpiresOn is not null && SecretNamePattern().IsMatch(s.Name))
            .Select(s => (Secret: s, Key: ConfigurationKey(s.Name)))
            .Where(s => configuration[s.Key] is not null)
            .Select(s => new SeedSecretExpiration(AppSettingName(s.Key), s.Secret.Name, s.Secret.ExpiresOn!.Value))
            .OrderBy(e => e.ExpiresOn)
            .ThenBy(e => e.Secret, StringComparer.Ordinal)
            .ToList();

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

    // Nur aus Platzhalter oder Referenz, die beide keinen geheimen Wert enthalten. Der Name geht in
    // die anonyme Health-Antwort und ins Log; aus dem Platzhalter zählt er deshalb nur, wenn er den
    // Regeln für Secret-Namen folgt, sonst bleibt er leer.
    private static string? SecretNameOf(string value)
    {
        if (value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
        {
            var name = value[PlaceholderPrefix.Length..].Trim();
            return SecretNamePattern().IsMatch(name) ? name : null;
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

/// <summary>Ein App-Setting, dessen Secret fehlt, bald abläuft oder abgelaufen ist.</summary>
/// <param name="Setting">Name des App-Settings in Azure, z. B. <c>Secrets__StripeKey</c>.</param>
/// <param name="Secret">Name des Secrets im Key Vault, soweit erkennbar.</param>
/// <param name="State">Was mit dem Secret ist.</param>
public sealed record SeedSecretProblem(string Setting, string? Secret, SeedSecretState State)
{
    /// <summary>Ablaufdatum aus dem Key Vault, nur bei <c>expiring</c> und <c>expired</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresOn { get; init; }
}

/// <summary>Warum ein Secret nicht nutzbar ist oder bald nicht mehr.</summary>
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

    /// <summary>
    /// Die aktuelle Version läuft in den nächsten 30 Tagen ab (<see cref="SeedSecretProblem.ExpiresOn"/>).
    /// Neuen Wert setzen, am besten mit neuem Ablaufdatum.
    /// </summary>
    [JsonStringEnumMemberName("expiring")]
    Expiring,

    /// <summary>Das Ablaufdatum der aktuellen Version ist überschritten.</summary>
    [JsonStringEnumMemberName("expired")]
    Expired,

    /// <summary>
    /// Die Ablaufdaten ließen sich nicht aus dem Vault lesen (<c>Seed__KeyVaultUri</c>); Einzelheiten
    /// stehen im Log. Die Secrets selbst können trotzdem funktionieren.
    /// </summary>
    [JsonStringEnumMemberName("expiryUnknown")]
    ExpiryUnknown,
}
