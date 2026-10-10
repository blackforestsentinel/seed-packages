using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Storage.Tests;

/// <summary>
/// Gegen Azurite, damit ETag-Verhalten und Kodierung echt geprüft sind. Läuft Azurite nicht,
/// werden die Tests übersprungen; in der CI (SEED_REQUIRE_AZURITE=true) schlagen sie fehl.
/// </summary>
public class AzuriteTests
{
    private const string ConnectionString = "UseDevelopmentStorage=true";

    private readonly SeedStorageClients clients;
    private readonly SeedTableRepository<TestEntity> repository;
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    public AzuriteTests()
    {
        RequireAzurite();
        clients = new SeedStorageClients(Options.Create(new SeedStorageOptions { ConnectionString = ConnectionString }));
        repository = new SeedTableRepository<TestEntity>(clients, $"t{Guid.NewGuid():N}");
    }

    [Fact]
    public async Task Add_SetsETag_SecondAddConflicts()
    {
        var added = await repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1", Name = "a" }, cancellationToken);

        Assert.NotEqual(default, added.ETag);
        var error = await Assert.ThrowsAsync<SeedConcurrencyException>(() =>
            repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1" }, cancellationToken));
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Update_WithStaleETag_Conflicts()
    {
        await repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1", Name = "a" }, cancellationToken);
        var mine = (await repository.GetAsync("p", "1", cancellationToken))!;
        var theirs = (await repository.GetAsync("p", "1", cancellationToken))!;

        theirs.Name = "b";
        await repository.UpdateAsync(theirs, cancellationToken);
        mine.Name = "c";
        var error = await Assert.ThrowsAsync<SeedConcurrencyException>(() => repository.UpdateAsync(mine, cancellationToken));

        Assert.Equal(412, error.Status);
        Assert.Equal("b", (await repository.GetAsync("p", "1", cancellationToken))!.Name);
    }

    [Fact]
    public async Task Update_WithCurrentETag_SetsNewETag()
    {
        var entity = await repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1", Count = 1 }, cancellationToken);
        var first = entity.ETag;

        entity.Count = 2;
        await repository.UpdateAsync(entity, cancellationToken);
        entity.Count = 3;
        await repository.UpdateAsync(entity, cancellationToken);

        Assert.NotEqual(first, entity.ETag);
        Assert.Equal(3, (await repository.GetAsync("p", "1", cancellationToken))!.Count);
    }

    [Fact]
    public async Task Update_OfDeletedRecord_Conflicts()
    {
        var entity = await repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1" }, cancellationToken);
        await repository.DeleteAsync(new TestEntity { PartitionKey = "p", RowKey = "1", ETag = ETag.All }, cancellationToken);

        var error = await Assert.ThrowsAsync<SeedConcurrencyException>(() => repository.UpdateAsync(entity, cancellationToken));

        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task Modify_RetriesAfterConcurrentChange()
    {
        await repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1", Count = 0 }, cancellationToken);
        var table = clients.Tables.GetTableClient(repository.TableName);
        var attempts = 0;

        var result = await repository.ModifyAsync("p", "1", entity =>
        {
            if (attempts++ == 0)
            {
                // Fremde Änderung zwischen Lesen und Schreiben
                table.UpdateEntity(new TestEntity { PartitionKey = "p", RowKey = "1", Count = 100 }, ETag.All, TableUpdateMode.Replace, cancellationToken);
            }

            entity.Count++;
            return true;
        }, cancellationToken);

        Assert.Equal(2, attempts);
        Assert.Equal(101, result!.Count);
    }

    [Fact]
    public async Task Modify_WithoutChange_DoesNotWrite_MissingRecordIsNull()
    {
        var entity = await repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1" }, cancellationToken);

        var unchanged = await repository.ModifyAsync("p", "1", _ => false, cancellationToken);

        Assert.Equal(entity.ETag, unchanged!.ETag);
        Assert.Null(await repository.ModifyAsync("p", "missing", _ => true, cancellationToken));
    }

    [Fact]
    public async Task Delete_WithStaleETag_Conflicts_ThenSucceedsOnce()
    {
        var stale = await repository.AddAsync(new TestEntity { PartitionKey = "p", RowKey = "1" }, cancellationToken);
        var staleETag = stale.ETag;
        stale.Count = 1;
        await repository.UpdateAsync(stale, cancellationToken);

        var error = await Assert.ThrowsAsync<SeedConcurrencyException>(() =>
            repository.DeleteAsync(new TestEntity { PartitionKey = "p", RowKey = "1", ETag = staleETag }, cancellationToken));

        Assert.Equal(412, error.Status);
        Assert.True(await repository.DeleteAsync(stale, cancellationToken));
        Assert.False(await repository.DeleteAsync(stale, cancellationToken));
    }

    [Fact]
    public async Task QueryPartition_ReturnsOnlyThatPartition()
    {
        await repository.UpsertAsync(new TestEntity { PartitionKey = "alice", RowKey = "1" }, cancellationToken);
        await repository.UpsertAsync(new TestEntity { PartitionKey = "alice", RowKey = "2" }, cancellationToken);
        await repository.UpsertAsync(new TestEntity { PartitionKey = "bob", RowKey = "1" }, cancellationToken);

        var rows = await repository.QueryPartitionAsync("alice", cancellationToken).ToListAsync(cancellationToken);

        Assert.Equal(["1", "2"], rows.Select(r => r.RowKey).Order());
        Assert.All(rows, r => Assert.NotEqual(default, r.ETag));
    }

    [Fact]
    public async Task QueueMessage_IsBase64EncodedJson_AsTheTriggerExpects()
    {
        var queueName = $"q-{Guid.NewGuid():N}";
        var sender = new SeedQueueSender(clients);

        await sender.SendAsync(queueName, new { JobId = "42", PartitionKey = "p" }, cancellationToken: cancellationToken);

        // Roh lesen, ohne Kodierung im Client: So sieht der Trigger die Nachricht.
        var raw = new QueueClient(ConnectionString, queueName);
        var message = (await raw.ReceiveMessageAsync(cancellationToken: cancellationToken)).Value;
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(message.Body.ToString()));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("42", document.RootElement.GetProperty("jobId").GetString());
    }

    [Fact]
    public async Task Container_IsCreatedLocally_BlobRoundTrip()
    {
        var container = await clients.GetContainerAsync($"c-{Guid.NewGuid():N}", cancellationToken);

        await container.UploadBlobAsync("hello.txt", BinaryData.FromString("hallo"), cancellationToken);
        var content = await container.GetBlobClient("hello.txt").DownloadContentAsync(cancellationToken);

        Assert.Equal("hallo", content.Value.Content.ToString());
    }

    private static void RequireAzurite()
    {
        var running = IsListening(10000) && IsListening(10001) && IsListening(10002);
        if (!running && string.Equals(Environment.GetEnvironmentVariable("SEED_REQUIRE_AZURITE"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("Azurite läuft nicht auf 127.0.0.1:10000-10002, ist aber verlangt (SEED_REQUIRE_AZURITE=true).");
        }

        Assert.SkipUnless(running, "Azurite läuft nicht (azurite --inMemoryPersistence --skipApiVersionCheck).");
    }

    private static bool IsListening(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
        }
        catch (AggregateException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
