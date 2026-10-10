using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace Bfs.Seed.Storage;

/// <summary>
/// Erzeugt die Clients für Table, Blob und Queue aus <see cref="SeedStorageOptions"/>: mit
/// Verbindungszeichenfolge (lokal Azurite) oder mit den Endpunkten und Entra-ID-Anmeldung.
/// </summary>
public static class SeedStorageClientFactory
{
    /// <summary>
    /// Anmeldung für die Endpunkte: Managed Identity mit der Client-ID aus den Optionen, ohne
    /// <c>credential</c> <c>DefaultAzureCredential</c> (lokal gegen einen echten Account).
    /// </summary>
    public static TokenCredential CreateCredential(SeedStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // In Azure ausdrücklich die UAMI der Function: DefaultAzureCredential würde mehrere
        // Wege durchprobieren und bei mehreren Identitäten nicht sicher die richtige wählen.
        if (options.UsesManagedIdentity)
        {
            return new ManagedIdentityCredential(string.IsNullOrWhiteSpace(options.ClientId)
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.FromUserAssignedClientId(options.ClientId));
        }

        return new DefaultAzureCredential();
    }

    /// <summary>Client für den Table-Dienst.</summary>
    public static TableServiceClient CreateTableServiceClient(SeedStorageOptions options, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsesConnectionString
            ? new TableServiceClient(options.ConnectionString)
            : new TableServiceClient(Require(options.TableServiceUri, "tableServiceUri"), credential ?? CreateCredential(options));
    }

    /// <summary>Client für den Blob-Dienst.</summary>
    public static BlobServiceClient CreateBlobServiceClient(SeedStorageOptions options, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsesConnectionString
            ? new BlobServiceClient(options.ConnectionString)
            : new BlobServiceClient(Require(options.BlobServiceUri, "blobServiceUri"), credential ?? CreateCredential(options));
    }

    /// <summary>
    /// Client für den Queue-Dienst. Nachrichten sind Base64-kodiert wie beim Queue-Trigger der
    /// Functions (Standard <c>messageEncoding: base64</c>); ohne Kodierung verwirft der Trigger sie.
    /// </summary>
    public static QueueServiceClient CreateQueueServiceClient(SeedStorageOptions options, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clientOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        return options.UsesConnectionString
            ? new QueueServiceClient(options.ConnectionString, clientOptions)
            : new QueueServiceClient(Require(options.QueueServiceUri, "queueServiceUri"), credential ?? CreateCredential(options), clientOptions);
    }

    private static Uri Require(Uri? uri, string setting) =>
        uri ?? throw new InvalidOperationException(
            $"Verbindung {SeedStorageOptions.ConnectionName}: {SeedStorageOptions.ConnectionName}__{setting} fehlt.");
}
