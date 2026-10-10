namespace Bfs.Seed.Storage;

/// <summary>
/// Optimistische Nebenläufigkeit: Der Datensatz wurde seit dem Lesen geändert oder gelöscht
/// (HTTP 412 bzw. 404) oder existiert beim Anlegen schon (409). Aufrufer laden neu und
/// entscheiden dann, statt fremde Änderungen still zu überschreiben.
/// </summary>
public sealed class SeedConcurrencyException : Exception
{
    /// <summary>Erzeugt die Ausnahme für einen Datensatz.</summary>
    public SeedConcurrencyException(string table, string partitionKey, string rowKey, int status, Exception? innerException = null)
        : base(CreateMessage(table, partitionKey, rowKey, status), innerException)
    {
        Table = table;
        PartitionKey = partitionKey;
        RowKey = rowKey;
        Status = status;
    }

    /// <summary>Tabelle.</summary>
    public string Table { get; }

    /// <summary>PartitionKey des Datensatzes.</summary>
    public string PartitionKey { get; }

    /// <summary>RowKey des Datensatzes.</summary>
    public string RowKey { get; }

    /// <summary>HTTP-Status der Ablehnung: 409 existiert schon, 412 geändert, 404 gelöscht.</summary>
    public int Status { get; }

    private static string CreateMessage(string table, string partitionKey, string rowKey, int status) => status switch
    {
        409 => $"{table}({partitionKey}, {rowKey}) existiert schon.",
        404 => $"{table}({partitionKey}, {rowKey}) wurde zwischenzeitlich gelöscht.",
        _ => $"{table}({partitionKey}, {rowKey}) wurde zwischenzeitlich geändert (ETag veraltet).",
    };
}
