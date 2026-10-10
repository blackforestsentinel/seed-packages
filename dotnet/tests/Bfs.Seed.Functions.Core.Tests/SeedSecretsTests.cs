using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bfs.Seed.Functions.Core.Tests;

public class SeedSecretsTests
{
    private const string Value = "sk_test_harmlos";
    private const string Unresolved = "@Microsoft.KeyVault(VaultName=kv-example-dev-abc123;SecretName=stripe-key)";

    private static SeedSecrets Secrets(params (string Key, string? Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Theory]
    [InlineData("stripe-key", "Secrets:StripeKey")]
    [InlineData("openai-api-key", "Secrets:OpenaiApiKey")]
    [InlineData("smtp2-password", "Secrets:Smtp2Password")]
    [InlineData("a", "Secrets:A")]
    public void ConfigurationKey_FollowsTerraformMapping(string secretName, string expected) =>
        Assert.Equal(expected, SeedSecrets.ConfigurationKey(secretName));

    [Theory]
    [InlineData("StripeKey")]
    [InlineData("stripe_key")]
    [InlineData("stripe--key")]
    [InlineData("api-2fa")]
    [InlineData("")]
    public void ConfigurationKey_RejectsInvalidNames(string secretName) =>
        Assert.Throws<ArgumentException>(() => SeedSecrets.ConfigurationKey(secretName));

    [Fact]
    public void Get_ReturnsResolvedValue() =>
        Assert.Equal(Value, Secrets(("Secrets:StripeKey", Value)).Get("stripe-key"));

    [Fact]
    public void Get_Missing_NamesSettingAndSecret()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Secrets().Get("stripe-key"));

        Assert.Contains("stripe-key", error.Message);
        Assert.Contains("Secrets__StripeKey", error.Message);
    }

    [Fact]
    public void Get_Placeholder_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Secrets(("Secrets:StripeKey", "seed-placeholder:stripe-key")).Get("stripe-key"));

        Assert.Contains("Platzhalter", error.Message);
        Assert.Contains("Secrets__StripeKey", error.Message);
    }

    [Fact]
    public void Get_UnresolvedReference_ThrowsWithoutReferenceText()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Secrets(("Secrets:StripeKey", Unresolved)).Get("stripe-key"));

        Assert.Contains("nicht aufgelöst", error.Message);
        Assert.DoesNotContain("@Microsoft.KeyVault", error.Message);
    }

    [Fact]
    public void Problems_FindsPlaceholdersAndUnresolvedReferencesInAllSettings()
    {
        var secrets = Secrets(
            ("Secrets:StripeKey", "seed-placeholder:stripe-key"),
            ("Secrets:SmtpPassword", Value),
            ("APPSETTING_Secrets:StripeKey", "seed-placeholder:stripe-key"),
            ("Ffh:Weclapp:Token", "@Microsoft.KeyVault(SecretUri=https://kv-x.vault.azure.net/secrets/weclapp-token/)"),
            ("Seed:Project", "kundenportal"));

        Assert.Equal(
            [
                new SeedSecretProblem("Ffh__Weclapp__Token", "weclapp-token", SeedSecretState.Unresolved),
                new SeedSecretProblem("Secrets__StripeKey", "stripe-key", SeedSecretState.Placeholder),
            ],
            secrets.Problems);
    }

    [Fact]
    public void Problems_EmptyWithoutSecrets() =>
        Assert.Empty(Secrets(("Seed:Project", "kundenportal")).Problems);

    [Fact]
    public void HealthReport_IsDegradedWithProblems_AndSerializesNamesOnly()
    {
        var secrets = Secrets(("Secrets:StripeKey", Unresolved), ("Secrets:SmtpPassword", Value));

        var report = SeedHealthReport.Create(new SeedOptions(), typeof(SeedSecretsTests).Assembly, secrets);
        var json = JsonSerializer.Serialize(report, JsonSerializerOptions.Web);

        Assert.Equal("degraded", report.Status);
        Assert.Contains("\"setting\":\"Secrets__StripeKey\"", json);
        Assert.Contains("\"secret\":\"stripe-key\"", json);
        Assert.Contains("\"state\":\"unresolved\"", json);
        Assert.DoesNotContain(Value, json);
        Assert.DoesNotContain("@Microsoft.KeyVault", json);
    }

    [Fact]
    public void HealthReport_IsOkWithoutProblems()
    {
        var report = SeedHealthReport.Create(new SeedOptions(), typeof(SeedSecretsTests).Assembly, Secrets(("Secrets:StripeKey", Value)));

        Assert.Equal("ok", report.Status);
        Assert.Empty(report.Secrets);
    }

    [Fact]
    public async Task StartupCheck_LogsNamesButNoValues()
    {
        var logger = new ListLogger<SeedSecretsStartupCheck>();
        var check = new SeedSecretsStartupCheck(
            Secrets(("Secrets:StripeKey", "seed-placeholder:stripe-key"), ("Secrets:SmtpPassword", Unresolved), ("Secrets:Other", Value)),
            logger);

        await check.StartAsync(CancellationToken.None);

        Assert.Equal([LogLevel.Error, LogLevel.Warning], logger.Entries.Select(e => e.Level));
        Assert.Contains(logger.Entries, e => e.Message.Contains("Secrets__StripeKey") && e.Message.Contains("stripe-key"));
        Assert.All(logger.Entries, e => Assert.DoesNotContain(Value, e.Message));
        Assert.All(logger.Entries, e => Assert.DoesNotContain("@Microsoft.KeyVault", e.Message));
    }

    [Fact]
    public void AddSeedCore_RegistersSecretsAndStartupCheck()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.Configuration["Secrets:StripeKey"] = Value;

        builder.AddSeedCore();

        using var provider = builder.Services.BuildServiceProvider();
        Assert.Equal(Value, provider.GetRequiredService<SeedSecrets>().Get("stripe-key"));
        // Nur die Registrierung: Die übrigen Hosted Services brauchen einen Functions-Host.
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SeedSecretsStartupCheck));
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
