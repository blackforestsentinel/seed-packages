using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bfs.Seed.Functions.Core.Tests;

public class SeedSecretExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualTime time = new() { Now = Now };

    private static IConfiguration Settings() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            // In Azure stehen hier die aufgelösten Werte; welches Secret wozu gehört, sagt nur der Name.
            ["Secrets:StripeKey"] = "sk_test_harmlos",
            ["Secrets:SmtpPassword"] = "harmlos",
            ["Secrets:OpenaiApiKey"] = "harmlos",
            ["Secrets:Webhook"] = "harmlos",
            ["Secrets:SmtpUser"] = "harmlos",
        })
        .Build();

    private static SeedVaultSecret[] Vault() =>
    [
        new("stripe-key", Now.AddDays(10)),
        new("smtp-password", Now.AddDays(-1)),
        new("openai-api-key", Now.AddDays(90)),
        new("webhook", null),
        // Kein Secret aus keyVault.secrets: kein App-Setting Secrets__ManuellAngelegt.
        new("manuell-angelegt", Now.AddDays(1)),
        // Deaktiviert: nicht nutzbar, sein Ablauf zählt nicht.
        new("smtp-user", Now.AddDays(2), Enabled: false),
    ];

    [Fact]
    public async Task HealthReport_NamesOnlyExpiringAndExpiredSecrets()
    {
        var secrets = Secrets(new FixedSource(Vault()));

        var report = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(report, JsonSerializerOptions.Web);

        Assert.Equal("degraded", report.Status);
        Assert.Equal(
            [
                new SeedSecretProblem("Secrets__SmtpPassword", "smtp-password", SeedSecretState.Expired) { ExpiresOn = Now.AddDays(-1) },
                new SeedSecretProblem("Secrets__StripeKey", "stripe-key", SeedSecretState.Expiring) { ExpiresOn = Now.AddDays(10) },
            ],
            report.Secrets);
        Assert.Contains("\"state\":\"expiring\",\"expiresOn\":\"2026-11-15T12:00:00+00:00\"", json);
        Assert.Contains("\"state\":\"expired\"", json);
        Assert.DoesNotContain("openai", json);
        Assert.DoesNotContain("webhook", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manuell", json);
        Assert.DoesNotContain("smtp-user", json);
        Assert.DoesNotContain("harmlos", json);
    }

    [Fact]
    public async Task HealthReport_NothingDue_StaysOk()
    {
        var secrets = Secrets(new FixedSource([new("openai-api-key", Now.AddDays(31)), new("webhook", null)]));

        var report = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);

        Assert.Equal("ok", report.Status);
        Assert.Empty(report.Secrets);
    }

    [Fact]
    public async Task HealthReport_KeepsPlaceholderProblems()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:StripeKey"] = "seed-placeholder:stripe-key" })
            .Build();
        var secrets = new SeedSecrets(configuration, new SeedSecretExpiryCache(new FixedSource([new("stripe-key", Now.AddDays(5))]), time, NullLogger.Instance), time);

        var report = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);

        Assert.Equal([SeedSecretState.Placeholder, SeedSecretState.Expiring], report.Secrets.Select(s => s.State));
    }

    [Fact]
    public async Task HealthReport_WithoutVault_IsLikeCreate()
    {
        var secrets = new SeedSecrets(Settings());

        var report = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);

        Assert.Equal(SeedHealthReport.Create(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets), report);
        Assert.Empty(await secrets.GetExpirationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HealthReport_VaultError_ReportsExpiryUnknownWithoutDetails()
    {
        var secrets = Secrets(new FailingSource("403 Forbidden: Caller is not authorized, vault kv-example-geheim"));

        var report = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(report, JsonSerializerOptions.Web);

        Assert.Equal("degraded", report.Status);
        Assert.Equal([new SeedSecretProblem("Seed__KeyVaultUri", null, SeedSecretState.ExpiryUnknown)], report.Secrets);
        Assert.Contains("\"state\":\"expiryUnknown\"", json);
        Assert.DoesNotContain("kv-example-geheim", json);
        Assert.DoesNotContain("expiresOn", json);
    }

    [Fact]
    public async Task HealthReport_SlowVault_ReportsExpiryUnknown_AndKeepsFetching()
    {
        var source = new PendingSource();
        var secrets = new SeedSecrets(Settings(), new SeedSecretExpiryCache(source, time, NullLogger.Instance), time, healthWait: TimeSpan.FromMilliseconds(50));

        var slow = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);
        source.Complete([new("stripe-key", Now.AddDays(3))]);
        var later = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);

        Assert.Equal(SeedSecretState.ExpiryUnknown, Assert.Single(slow.Secrets).State);
        Assert.Equal(SeedSecretState.Expiring, Assert.Single(later.Secrets).State);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task GetExpirations_ListsAllManagedSecretsWithDate()
    {
        var secrets = Secrets(new FixedSource(Vault()));

        var expirations = await secrets.GetExpirationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new SeedSecretExpiration("Secrets__SmtpPassword", "smtp-password", Now.AddDays(-1)),
                new SeedSecretExpiration("Secrets__StripeKey", "stripe-key", Now.AddDays(10)),
                new SeedSecretExpiration("Secrets__OpenaiApiKey", "openai-api-key", Now.AddDays(90)),
            ],
            expirations);
    }

    [Fact]
    public async Task GetExpirations_VaultError_Throws()
    {
        var secrets = Secrets(new FailingSource("kaputt"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => secrets.GetExpirationsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Seed__KeyVaultUri", error.Message);
        Assert.Equal("kaputt", error.InnerException?.Message);
    }

    [Fact]
    public async Task Cache_ReadsTheVaultAtMostOncePerHour()
    {
        var source = new FixedSource(Vault());
        var secrets = Secrets(source);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => secrets.GetExpirationsAsync(TestContext.Current.CancellationToken)));
        time.Now = Now.AddMinutes(59);
        await secrets.GetExpirationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, source.Calls);

        time.Now = Now.AddHours(1);
        await secrets.GetExpirationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Cache_RetriesAnErrorAfterFiveMinutes()
    {
        var source = new FailingSource("kaputt");
        var secrets = Secrets(source);

        await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);
        time.Now = Now.AddMinutes(4);
        await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);
        Assert.Equal(1, source.Calls);

        time.Now = Now.AddMinutes(5);
        await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task KeyVaultSource_ListsMetadataOnly_AcrossPages()
    {
        var vault = new FakeVault();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:KeyVaultUri"] = FakeVault.Uri })
            .Build();
        var options = new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(vault)) };
        options.Retry.MaxRetries = 0;

        var source = SeedKeyVaultSecretSource.FromConfiguration(configuration, new FakeCredential(), options);
        var secrets = await source!.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new SeedVaultSecret("stripe-key", DateTimeOffset.FromUnixTimeSeconds(1767225600)),
                new SeedVaultSecret("webhook", null, Enabled: false),
                new SeedVaultSecret("smtp-password", DateTimeOffset.FromUnixTimeSeconds(1764547200)),
            ],
            secrets);
        // Nur die Liste (Metadaten), nie GET /secrets/<name> mit dem Wert.
        Assert.All(vault.Paths, p => Assert.Equal("/secrets", p.TrimEnd('/')));
        Assert.Equal("Bearer token", vault.Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FromConfiguration_WithoutSetting_HasNoSource(string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:KeyVaultUri"] = value })
            .Build();

        Assert.Null(SeedKeyVaultSecretSource.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData("kv-example")]
    [InlineData("http://kv-example.vault.azure.net/")]
    public async Task FromConfiguration_InvalidAddress_FailsOnRead(string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:KeyVaultUri"] = value })
            .Build();

        var source = SeedKeyVaultSecretSource.FromConfiguration(configuration);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source!.ListAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Seed__KeyVaultUri", error.Message);
    }

    [Fact]
    public async Task AddSeedCore_WithKeyVaultUri_ReadsExpirations()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.Configuration["Seed:KeyVaultUri"] = "kein-https";
        builder.AddSeedCore();

        // Mit Vault löst SeedSecrets das Logging auf; dessen Telemetrie lässt sich nur asynchron freigeben.
        await using var provider = builder.Services.BuildServiceProvider();
        var secrets = provider.GetRequiredService<SeedSecrets>();

        // Ohne erreichbaren Vault: Die Einstellung wirkt, der Report meldet expiryUnknown.
        var report = await SeedHealthReport.CreateAsync(new SeedOptions(), typeof(SeedSecretExpiryTests).Assembly, secrets, TestContext.Current.CancellationToken);
        Assert.Equal(SeedSecretState.ExpiryUnknown, Assert.Single(report.Secrets).State);
    }

    private SeedSecrets Secrets(ISeedVaultSecretSource source) =>
        new(Settings(), new SeedSecretExpiryCache(source, time, NullLogger.Instance), time);

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedSource(SeedVaultSecret[] secrets) : ISeedVaultSecretSource
    {
        private int calls;

        public int Calls => calls;

        public Task<IReadOnlyList<SeedVaultSecret>> ListAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<SeedVaultSecret>>(secrets);
        }
    }

    private sealed class FailingSource(string message) : ISeedVaultSecretSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SeedVaultSecret>> ListAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromException<IReadOnlyList<SeedVaultSecret>>(new InvalidOperationException(message));
        }
    }

    private sealed class PendingSource : ISeedVaultSecretSource
    {
        private readonly TaskCompletionSource<IReadOnlyList<SeedVaultSecret>> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public Task<IReadOnlyList<SeedVaultSecret>> ListAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return pending.Task;
        }

        public void Complete(IReadOnlyList<SeedVaultSecret> secrets) => pending.SetResult(secrets);
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Key Vault mit Challenge wie im Original: erst 401 ohne Token, dann zwei Seiten.</summary>
    private sealed class FakeVault : HttpMessageHandler
    {
        public const string Uri = "https://kv-example.vault.azure.net/";

        public List<string> Paths { get; } = [];

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Headers.Authorization is null)
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                challenge.Headers.TryAddWithoutValidation(
                    "WWW-Authenticate",
                    "Bearer authorization=\"https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000\", resource=\"https://vault.azure.net\"");
                return Task.FromResult(challenge);
            }

            Authorization = request.Headers.Authorization.ToString();
            var json = request.RequestUri.Query.Contains("skiptoken", StringComparison.Ordinal)
                ? """{"value":[{"id":"https://kv-example.vault.azure.net/secrets/smtp-password","attributes":{"enabled":true,"exp":1764547200}}]}"""
                : """
                  {"value":[
                    {"id":"https://kv-example.vault.azure.net/secrets/stripe-key","attributes":{"enabled":true,"exp":1767225600}},
                    {"id":"https://kv-example.vault.azure.net/secrets/webhook","attributes":{"enabled":false}}],
                   "nextLink":"https://kv-example.vault.azure.net/secrets?api-version=7.6&$skiptoken=seite2&maxresults=25"}
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
