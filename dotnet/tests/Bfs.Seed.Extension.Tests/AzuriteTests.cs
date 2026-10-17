using System.Net.Sockets;
using Bfs.Seed.Storage;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Extension.Tests;

/// <summary>
/// SeedBlobExtensionReleases gegen Azurite, mit current.json und CRX so, wie die Pipeline sie
/// ablegt. Läuft Azurite nicht, werden die Tests übersprungen (in der CI schlagen sie fehl).
/// </summary>
public class AzuriteTests
{
    private const string ConnectionString = "UseDevelopmentStorage=true";

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly SeedStorageClients clients;
    private readonly SeedExtensionOptions options = new()
    {
        PackageName = "sentinel-auth",
        ContainerName = $"ext-{Guid.NewGuid():N}",
    };

    private readonly ManualTime time = new();

    public AzuriteTests()
    {
        RequireAzurite();
        clients = new SeedStorageClients(Options.Create(new SeedStorageOptions { ConnectionString = ConnectionString }));
    }

    [Fact]
    public async Task Current_IsNull_UntilPublished_ThenReadAndCached()
    {
        var releases = new SeedBlobExtensionReleases(clients, Options.Create(options), time);
        Assert.Null(await releases.GetCurrentAsync(cancellationToken));

        // Die Pipeline schreibt weitere Felder; sie werden ignoriert.
        await Upload(SeedExtension.CurrentFile, """{"id":"aaiejlkbnaaolchagdkohjfomnhglfac","version":"1.0.0.1","file":"sentinel-auth-1.0.0.1.crx","sha256":"ab","contentHash":"x","buildId":"1"}""");
        Assert.Null(await releases.GetCurrentAsync(cancellationToken));

        time.Advance(options.CacheDuration);
        var release = await releases.GetCurrentAsync(cancellationToken);

        Assert.Equal(new SeedExtensionRelease("aaiejlkbnaaolchagdkohjfomnhglfac", "1.0.0.1", "sentinel-auth-1.0.0.1.crx", "ab"), release);
    }

    [Fact]
    public async Task Package_IsStreamed_OnlyForValidNames()
    {
        var releases = new SeedBlobExtensionReleases(clients, Options.Create(options), time);
        await Upload("sentinel-auth-1.0.0.1.crx", "Cr24-package");
        await Upload(SeedExtension.CurrentFile, "{}");

        await using (var stream = await releases.OpenPackageAsync("sentinel-auth-1.0.0.1.crx", cancellationToken))
        {
            using var reader = new StreamReader(stream!);
            Assert.Equal("Cr24-package", await reader.ReadToEndAsync(cancellationToken));
        }

        Assert.Null(await releases.OpenPackageAsync("sentinel-auth-1.0.0.2.crx", cancellationToken));
        Assert.Null(await releases.OpenPackageAsync(SeedExtension.CurrentFile, cancellationToken));
    }

    private async Task Upload(string name, string content)
    {
        var container = await clients.GetContainerAsync(options.ContainerName, cancellationToken);
        await container.GetBlobClient(name).UploadAsync(BinaryData.FromString(content), overwrite: true, cancellationToken);
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

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
