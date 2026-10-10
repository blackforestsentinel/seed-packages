using System.Collections.Concurrent;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Storage;

/// <summary>
/// Clients für den Storage Account des Projekts, einmal je App erzeugt. In Azure legt
/// Terraform Tabellen, Container und Queues aus <c>project.yaml</c> an; lokal (Azurite) legen
/// die <c>Get…Async</c>-Methoden sie beim ersten Zugriff an.
/// </summary>
public sealed class SeedStorageClients
{
    private readonly bool createIfNotExists;
    private readonly ConcurrentDictionary<string, Lazy<Task>> created = new(StringComparer.Ordinal);

    /// <summary>Erzeugt die Clients aus den Optionen.</summary>
    public SeedStorageClients(IOptions<SeedStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var o = options.Value;

        // Eine Anmeldung für alle drei Dienste, damit Tokens gemeinsam zwischengespeichert werden.
        var credential = o.UsesConnectionString ? null : SeedStorageClientFactory.CreateCredential(o);
        Tables = SeedStorageClientFactory.CreateTableServiceClient(o, credential);
        Blobs = SeedStorageClientFactory.CreateBlobServiceClient(o, credential);
        Queues = SeedStorageClientFactory.CreateQueueServiceClient(o, credential);

        // In Azure nicht: Ein Tippfehler im Namen soll als Fehler auffallen und nicht still eine
        // Tabelle anlegen, die project.yaml nicht kennt.
        createIfNotExists = o.UsesConnectionString;
    }

    /// <summary>Table-Dienst.</summary>
    public TableServiceClient Tables { get; }

    /// <summary>Blob-Dienst.</summary>
    public BlobServiceClient Blobs { get; }

    /// <summary>Queue-Dienst; Nachrichten sind Base64-kodiert wie beim Queue-Trigger.</summary>
    public QueueServiceClient Queues { get; }

    /// <summary>Client einer Tabelle; lokal wird sie beim ersten Zugriff angelegt.</summary>
    public async ValueTask<TableClient> GetTableAsync(string name, CancellationToken cancellationToken = default)
    {
        var table = Tables.GetTableClient(name);
        await EnsureAsync($"table:{name}", () => table.CreateIfNotExistsAsync(cancellationToken));
        return table;
    }

    /// <summary>Client eines Blob-Containers; lokal wird er beim ersten Zugriff angelegt.</summary>
    public async ValueTask<BlobContainerClient> GetContainerAsync(string name, CancellationToken cancellationToken = default)
    {
        var container = Blobs.GetBlobContainerClient(name);
        await EnsureAsync($"container:{name}", () => container.CreateIfNotExistsAsync(cancellationToken: cancellationToken));
        return container;
    }

    /// <summary>Client einer Queue; lokal wird sie beim ersten Zugriff angelegt.</summary>
    public async ValueTask<QueueClient> GetQueueAsync(string name, CancellationToken cancellationToken = default)
    {
        var queue = Queues.GetQueueClient(name);
        await EnsureAsync($"queue:{name}", () => queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken));
        return queue;
    }

    private async Task EnsureAsync(string key, Func<Task> create)
    {
        if (!createIfNotExists)
        {
            return;
        }

        var task = created.GetOrAdd(key, _ => new Lazy<Task>(create)).Value;
        try
        {
            await task;
        }
        catch
        {
            // Nach einem Fehler (etwa Azurite noch nicht gestartet) beim nächsten Zugriff erneut versuchen.
            created.TryRemove(key, out _);
            throw;
        }
    }
}
