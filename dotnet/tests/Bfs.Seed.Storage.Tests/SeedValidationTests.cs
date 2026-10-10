using Azure;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Storage.Tests;

/// <summary>Prüfungen, die vor jedem Netzwerkzugriff greifen; ohne Azurite.</summary>
public class SeedValidationTests
{
    private static readonly SeedStorageClients Clients =
        new(Options.Create(new SeedStorageOptions { ConnectionString = "UseDevelopmentStorage=true" }));

    private readonly SeedTableRepository<TestEntity> repository = new(Clients, "jobs");

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a#b")]
    [InlineData("a?b")]
    [InlineData("a\tb")]
    public async Task InvalidKey_IsRejectedWithFieldName(string key)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.AddAsync(new TestEntity { PartitionKey = key, RowKey = "1" }, TestContext.Current.CancellationToken));

        Assert.Contains("PartitionKey", error.Message);
    }

    [Fact]
    public async Task UpdateWithoutETag_IsRejected()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.UpdateAsync(new TestEntity { PartitionKey = "p", RowKey = "1" }, TestContext.Current.CancellationToken));

        Assert.Contains("ETag", error.Message);
    }

    [Fact]
    public async Task DeleteWithoutETag_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.DeleteAsync(new TestEntity { PartitionKey = "p", RowKey = "1" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void InvalidTableName_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SeedTableRepository<TestEntity>(Clients, "job-status"));
    }

    [Fact]
    public void QueueMessage_IsCamelCaseJson_StringUnchanged()
    {
        Assert.Equal("""{"jobId":"42","count":3}""", SeedQueueSender.Serialize(new { JobId = "42", Count = 3 }));
        Assert.Equal("plain text", SeedQueueSender.Serialize("plain text"));
    }

    [Fact]
    public async Task OversizedQueueMessage_IsRejected()
    {
        var sender = new SeedQueueSender(Clients);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            sender.SendAsync("jobs", new string('x', SeedQueueSender.MaxMessageBytes + 1), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Blob", error.Message);
    }

    [Fact]
    public void ConcurrencyException_NamesTheRecord()
    {
        var error = new SeedConcurrencyException("jobs", "p", "1", 412, new RequestFailedException(412, "Precondition Failed"));

        Assert.Equal(("jobs", "p", "1", 412), (error.Table, error.PartitionKey, error.RowKey, error.Status));
        Assert.Contains("ETag", error.Message);
    }
}
