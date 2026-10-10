using System.Text;
using System.Text.Json;

namespace Bfs.Seed.Storage;

/// <summary>Stellt Nachrichten in eine Queue, passend zum Queue-Trigger der Functions.</summary>
public interface ISeedQueueSender
{
    /// <summary>
    /// Stellt <paramref name="message"/> als JSON (camelCase) in die Queue; ein <c>string</c> geht
    /// unverändert. Der Trigger bindet die Nachricht wieder an denselben Typ. Ergebnis: Message-ID.
    /// </summary>
    /// <param name="queueName">Name der Queue aus <c>project.yaml</c>.</param>
    /// <param name="message">Inhalt, höchstens 48 KiB als UTF-8 (64 KiB nach Base64).</param>
    /// <param name="visibilityDelay">Erst nach dieser Zeit sichtbar, z. B. für einen späteren Versuch.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    Task<string> SendAsync<T>(string queueName, T message, TimeSpan? visibilityDelay = null, CancellationToken cancellationToken = default);
}

/// <summary>Implementierung von <see cref="ISeedQueueSender"/>.</summary>
/// <remarks>
/// Base64 kommt aus dem Client (<see cref="SeedStorageClientFactory.CreateQueueServiceClient"/>):
/// Der Trigger erwartet per Default Base64 und schiebt andere Nachrichten nach mehreren
/// Fehlversuchen in die Poison-Queue.
/// </remarks>
public sealed class SeedQueueSender(SeedStorageClients clients) : ISeedQueueSender
{
    /// <summary>Größte Nachricht vor der Base64-Kodierung; Azure erlaubt 64 KiB danach.</summary>
    internal const int MaxMessageBytes = 48 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<string> SendAsync<T>(string queueName, T message, TimeSpan? visibilityDelay = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(message);

        var body = Serialize(message);
        if (Encoding.UTF8.GetByteCount(body) > MaxMessageBytes)
        {
            throw new ArgumentException($"Nachricht für {queueName} ist größer als 48 KiB. Große Daten in einen Blob legen und nur den Verweis senden.", nameof(message));
        }

        var queue = await clients.GetQueueAsync(queueName, cancellationToken);
        var receipt = await queue.SendMessageAsync(body, visibilityDelay, timeToLive: null, cancellationToken);
        return receipt.Value.MessageId;
    }

    internal static string Serialize<T>(T message) =>
        message as string ?? JsonSerializer.Serialize(message, JsonOptions);
}
