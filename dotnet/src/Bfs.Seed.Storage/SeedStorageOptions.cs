namespace Bfs.Seed.Storage;

/// <summary>
/// Verbindung zum Storage Account des Projekts. Das Terraform-Modul storage setzt dafür die
/// App-Settings <c>SeedStorage__blobServiceUri</c>, <c>__queueServiceUri</c>,
/// <c>__tableServiceUri</c>, <c>__credential</c> (<c>managedidentity</c>) und <c>__clientId</c>.
/// Lokal genügt <c>SeedStorage = UseDevelopmentStorage=true</c> für Azurite.
/// </summary>
public sealed class SeedStorageOptions
{
    /// <summary>
    /// Name der Verbindung und des Konfigurationsabschnitts. Trigger nutzen denselben Namen:
    /// <c>[QueueTrigger("jobs", Connection = SeedStorageOptions.ConnectionName)]</c>.
    /// </summary>
    public const string ConnectionName = "SeedStorage";

    /// <summary>Wert von <c>credential</c> für die Anmeldung per Managed Identity.</summary>
    public const string ManagedIdentityCredential = "managedidentity";

    /// <summary>
    /// Verbindungszeichenfolge, lokal <c>UseDevelopmentStorage=true</c>. Steht direkt im Wert
    /// von <c>SeedStorage</c> und hat Vorrang vor den Endpunkten.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Blob-Endpunkt, z. B. <c>https://&lt;konto&gt;.blob.core.windows.net/</c>.</summary>
    public Uri? BlobServiceUri { get; set; }

    /// <summary>Queue-Endpunkt.</summary>
    public Uri? QueueServiceUri { get; set; }

    /// <summary>Table-Endpunkt.</summary>
    public Uri? TableServiceUri { get; set; }

    /// <summary>
    /// <c>managedidentity</c> in Azure. Leer: <c>DefaultAzureCredential</c>, etwa lokal gegen
    /// einen echten Account mit der Anmeldung aus <c>az login</c> (wie der Functions-Host).
    /// </summary>
    public string? Credential { get; set; }

    /// <summary>Client-ID der User-Assigned Managed Identity; leer für die System-Assigned Identity.</summary>
    public string? ClientId { get; set; }

    /// <summary>Lokaler Betrieb (Azurite): Tabellen, Queues und Container entstehen beim ersten Zugriff.</summary>
    internal bool UsesConnectionString => !string.IsNullOrWhiteSpace(ConnectionString);

    internal bool UsesManagedIdentity => string.Equals(Credential, ManagedIdentityCredential, StringComparison.OrdinalIgnoreCase);

    internal bool IsComplete =>
        UsesConnectionString
        || (BlobServiceUri is { IsAbsoluteUri: true } && QueueServiceUri is { IsAbsoluteUri: true } && TableServiceUri is { IsAbsoluteUri: true }
            && (string.IsNullOrWhiteSpace(Credential) || UsesManagedIdentity));
}
