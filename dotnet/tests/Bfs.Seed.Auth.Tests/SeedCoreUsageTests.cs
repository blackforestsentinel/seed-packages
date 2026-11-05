using Bfs.Seed.Functions.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Auth.Tests;

// Functions für die Prüfung in AddSeedCore(): Sie liegen in dieser Test-Assembly, die beim Testlauf
// die Entry-Assembly ist. Die Capabilities kennt SeedCapabilityMapTests.ProjectYaml.

public sealed class CoreUsageFunctions
{
    [Function("ListInvoices")]
    [RequireCapability("invoices.read")]
    public string List() => "list";

    [Function("Ping")]
    public string Ping() => "pong";
}

[RequireScope("mcp_access")]
public sealed class CoreUsageScopedFunctions
{
    [Function("Tools")]
    public string Tools() => "tools";
}

// [RequireAnyRole] und [AllowWithoutRole] versprechen eine Anmeldung; ohne Token-Prüfung wären sie anonym.
public sealed class CoreUsageRoleFunctions
{
    [Function("Orders")]
    [RequireAnyRole]
    public string Orders() => "orders";

    [Function("WhoAmI")]
    [AllowWithoutRole]
    public string WhoAmI() => "me";
}

public sealed class SeedCoreUsageTests
{
    [Fact]
    public void WithoutUseSeedAuth_ProtectedFunctions_DoNotStart()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.AddSeedCore();

        var error = Assert.Throws<OptionsValidationException>(() => Resolve(builder));

        Assert.Contains("ListInvoices, Tools, Orders, WhoAmI", error.Message);
        Assert.DoesNotContain("Ping", error.Message);
    }

    [Fact]
    public void WithoutUseSeedAuth_DisabledFunctions_DoNotCount()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.Configuration["AzureWebJobs.ListInvoices.Disabled"] = "true";
        builder.Configuration["AzureWebJobs.Tools.Disabled"] = "1";
        builder.Configuration["AzureWebJobs.Orders.Disabled"] = "true";
        builder.Configuration["AzureWebJobs.WhoAmI.Disabled"] = "true";
        builder.AddSeedCore();

        Assert.NotNull(Resolve(builder));
    }

    [Fact]
    public void WithoutUseSeedAuth_FunctionDisabledWithFalse_StillCounts()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.Configuration["AzureWebJobs.ListInvoices.Disabled"] = "false";
        builder.Configuration["AzureWebJobs.Tools.Disabled"] = "true";
        builder.Configuration["AzureWebJobs.Orders.Disabled"] = "true";
        builder.Configuration["AzureWebJobs.WhoAmI.Disabled"] = "true";
        builder.AddSeedCore();

        var error = Assert.Throws<OptionsValidationException>(() => Resolve(builder));

        Assert.Contains("Functions ListInvoices verlangen", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithUseSeedAuth_Starts(bool authFirst)
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        if (authFirst)
        {
            builder.UseSeedAuth();
            builder.AddSeedCore();
        }
        else
        {
            builder.AddSeedCore();
            builder.UseSeedAuth();
        }

        Assert.NotNull(Resolve(builder));
    }

    private static SeedOptions Resolve(FunctionsApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SeedOptions>>().Value;
    }
}
