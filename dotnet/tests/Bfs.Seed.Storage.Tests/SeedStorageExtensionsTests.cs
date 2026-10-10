using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Storage.Tests;

public class SeedStorageExtensionsTests
{
    [Fact]
    public void ConnectionString_UsesAzuriteEndpoints()
    {
        using var provider = Build(new() { ["SeedStorage"] = "UseDevelopmentStorage=true" });

        Assert.Equal(10002, provider.GetRequiredService<TableServiceClient>().Uri.Port);
        Assert.Equal(10000, provider.GetRequiredService<BlobServiceClient>().Uri.Port);
        Assert.Equal(10001, provider.GetRequiredService<QueueServiceClient>().Uri.Port);
        Assert.NotNull(provider.GetRequiredService<ISeedQueueSender>());
    }

    [Fact]
    public void ServiceUris_FromTerraformAppSettings()
    {
        using var provider = Build(new()
        {
            ["SeedStorage:blobServiceUri"] = "https://stexampledata123456.blob.core.windows.net/",
            ["SeedStorage:queueServiceUri"] = "https://stexampledata123456.queue.core.windows.net/",
            ["SeedStorage:tableServiceUri"] = "https://stexampledata123456.table.core.windows.net/",
            ["SeedStorage:credential"] = "managedidentity",
            ["SeedStorage:clientId"] = "33333333-3333-3333-3333-333333333333",
        });

        var options = provider.GetRequiredService<IOptions<SeedStorageOptions>>().Value;
        Assert.False(options.UsesConnectionString);
        Assert.Equal("stexampledata123456.table.core.windows.net", provider.GetRequiredService<TableServiceClient>().Uri.Host);
        Assert.Equal("stexampledata123456.blob.core.windows.net", provider.GetRequiredService<BlobServiceClient>().Uri.Host);
        Assert.Equal("stexampledata123456.queue.core.windows.net", provider.GetRequiredService<QueueServiceClient>().Uri.Host);
        Assert.IsType<ManagedIdentityCredential>(SeedStorageClientFactory.CreateCredential(options));
    }

    [Fact]
    public void WithoutCredentialSetting_UsesDefaultAzureCredential()
    {
        var options = new SeedStorageOptions { TableServiceUri = new Uri("https://example.table.core.windows.net/") };

        Assert.IsType<DefaultAzureCredential>(SeedStorageClientFactory.CreateCredential(options));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://example.blob.core.windows.net/", null)]
    [InlineData("https://example.blob.core.windows.net/", "connectionstring")]
    public void IncompleteConnection_FailsValidation(string? blobUri, string? credential)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SeedStorage:blobServiceUri"] = blobUri,
            ["SeedStorage:queueServiceUri"] = blobUri?.Replace("blob", "queue"),
            ["SeedStorage:tableServiceUri"] = credential is null ? null : blobUri?.Replace("blob", "table"),
            ["SeedStorage:credential"] = credential,
        };
        using var provider = Build(settings);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SeedStorageOptions>>().Value);
        Assert.Contains("project.yaml", error.Message);
    }

    [Fact]
    public void AddSeedStorage_TwiceRegistersOnce()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.Configuration["SeedStorage"] = "UseDevelopmentStorage=true";

        builder.AddSeedStorage().AddSeedStorage();

        Assert.Single(builder.Services, d => d.ServiceType == typeof(SeedStorageClients));
        Assert.Single(builder.Services, d => d.ServiceType == typeof(ISeedQueueSender));
    }

    [Fact]
    public void AddSeedTable_RegistersRepositoryForTable()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.Configuration["SeedStorage"] = "UseDevelopmentStorage=true";
        builder.AddSeedStorage();
        builder.Services.AddSeedTable<TestEntity>("jobs");

        using var provider = builder.Services.BuildServiceProvider();

        Assert.Equal("jobs", provider.GetRequiredService<ISeedTableRepository<TestEntity>>().TableName);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("1jobs")]
    [InlineData("job-status")]
    public void AddSeedTable_RejectsInvalidName(string name)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSeedTable<TestEntity>(name));
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        foreach (var (key, value) in settings)
        {
            builder.Configuration[key] = value;
        }

        builder.AddSeedStorage();
        return builder.Services.BuildServiceProvider();
    }
}
