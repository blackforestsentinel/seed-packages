using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bfs.Seed.Functions.Core;

/// <summary>Ablaufdatum eines Secrets aus <c>keyVault.secrets</c>, wie es im Key Vault steht.</summary>
/// <param name="Setting">Name des App-Settings in Azure, z. B. <c>Secrets__StripeKey</c>.</param>
/// <param name="Secret">Name des Secrets im Key Vault, z. B. <c>stripe-key</c>.</param>
/// <param name="ExpiresOn">Ablaufdatum der aktuellen Version (Attribut <c>exp</c>).</param>
public sealed record SeedSecretExpiration(string Setting, string Secret, DateTimeOffset ExpiresOn);

/// <summary>Name und Ablaufdatum eines Secrets im Vault, ohne Wert.</summary>
internal readonly record struct SeedVaultSecret(string Name, DateTimeOffset? ExpiresOn);

/// <summary>Liest die Secrets eines Vaults samt Ablaufdatum, nie ihre Werte.</summary>
internal interface ISeedVaultSecretSource
{
    Task<IReadOnlyList<SeedVaultSecret>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Liest die Metadaten aller Secrets mit einem Aufruf (List Secrets, je Seite bis zu 25). Dafür
/// genügt die Rolle Key Vault Secrets User, die die Function für die Key-Vault-Referenzen ohnehin
/// hat: Sie enthält <c>secrets/readMetadata/action</c>. Werte überträgt der Aufruf nicht.
/// </summary>
internal sealed class SeedKeyVaultSecretSource(SecretClient client) : ISeedVaultSecretSource
{
    public async Task<IReadOnlyList<SeedVaultSecret>> ListAsync(CancellationToken cancellationToken)
    {
        var secrets = new List<SeedVaultSecret>();
        await foreach (var properties in client.GetPropertiesOfSecretsAsync(cancellationToken))
        {
            secrets.Add(new SeedVaultSecret(properties.Name, properties.ExpiresOn));
        }

        return secrets;
    }

    /// <summary>
    /// Quelle für den Vault aus <see cref="SeedSecrets.KeyVaultUriKey"/>; <c>null</c> ohne Einstellung.
    /// Eine ungültige Adresse verhindert den Start nicht, der Health-Report meldet sie.
    /// </summary>
    internal static ISeedVaultSecretSource? FromConfiguration(IConfiguration configuration, TokenCredential? credential = null, SecretClientOptions? options = null)
    {
        var value = configuration[SeedSecrets.KeyVaultUriKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return new InvalidSource($"{SeedSecrets.KeyVaultUriSetting} ist keine https-Adresse eines Key Vaults.");
        }

        return new SeedKeyVaultSecretSource(new SecretClient(uri, credential ?? CreateCredential(configuration), options));
    }

    // In Azure ausdrücklich die UAMI der Function (AZURE_CLIENT_ID setzt das Modul core), wie in
    // Bfs.Seed.Storage: DefaultAzureCredential probierte sonst mehrere Wege durch. Lokal die Anmeldung
    // der Entwicklerin, etwa aus az login.
    private static TokenCredential CreateCredential(IConfiguration configuration) =>
        configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new DefaultAzureCredential();

    private sealed class InvalidSource(string message) : ISeedVaultSecretSource
    {
        public Task<IReadOnlyList<SeedVaultSecret>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SeedVaultSecret>>(new InvalidOperationException(message));
    }
}

/// <summary>Stand der Ablaufdaten: die Secrets oder der Fehler beim Lesen, und bis wann er gilt.</summary>
internal sealed record SeedSecretExpirySnapshot(IReadOnlyList<SeedVaultSecret> Secrets, Exception? Error, DateTimeOffset ValidUntil);

/// <summary>
/// Hält die Ablaufdaten eine Stunde, einen Fehler fünf Minuten. Gleichzeitige Anfragen teilen sich
/// einen Aufruf. So erreicht der anonyme Health-Endpunkt den Vault höchstens einmal je Zeitraum und
/// Instanz, egal wie oft ihn jemand aufruft.
/// </summary>
internal sealed class SeedSecretExpiryCache(ISeedVaultSecretSource source, TimeProvider time, ILogger logger)
{
    internal static readonly TimeSpan SuccessLifetime = TimeSpan.FromHours(1);
    internal static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(5);

    // Obergrenze für einen hängenden Aufruf; das SDK wiederholt bei 429 und 5xx selbst.
    internal static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();
    private Task<SeedSecretExpirySnapshot>? current;

    public Task<SeedSecretExpirySnapshot> GetAsync()
    {
        lock (gate)
        {
            if (current is null || (current.IsCompleted && time.GetUtcNow() >= current.Result.ValidUntil))
            {
                current = FetchAsync();
            }

            return current;
        }
    }

    private async Task<SeedSecretExpirySnapshot> FetchAsync()
    {
        // Erst nach dem Lock weiter: Token und erster Aufruf sollen ihn nicht halten.
        await Task.Yield();

        using var timeout = new CancellationTokenSource(FetchTimeout, time);
        try
        {
            var secrets = await source.ListAsync(timeout.Token);
            return new SeedSecretExpirySnapshot(secrets, null, time.GetUtcNow() + SuccessLifetime);
        }
        catch (Exception e)
        {
            // Der Health-Report bleibt erreichbar und meldet expiryUnknown; die Einzelheiten stehen nur im Log.
            logger.LogWarning(e, "Ablaufdaten der Secrets aus dem Key Vault ({Setting}) nicht lesbar. Rolle Key Vault Secrets User der Function und die Adresse prüfen.",
                SeedSecrets.KeyVaultUriSetting);
            return new SeedSecretExpirySnapshot([], e, time.GetUtcNow() + FailureLifetime);
        }
    }
}
