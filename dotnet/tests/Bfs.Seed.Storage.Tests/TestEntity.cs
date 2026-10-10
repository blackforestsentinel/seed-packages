using Azure;
using Azure.Data.Tables;

namespace Bfs.Seed.Storage.Tests;

public sealed class TestEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string? Name { get; set; }

    public int Count { get; set; }
}
