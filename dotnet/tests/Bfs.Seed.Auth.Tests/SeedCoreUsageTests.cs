using System.Reflection;
using Bfs.Seed.Functions.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Auth.Tests;

// Functions für die Prüfung in AddSeedCore(): Sie liegen in dieser Test-Assembly, die beim Testlauf
// die Entry-Assembly ist. Die Capabilities kennt SeedCapabilityMapTests.ProjectYaml. Die Functions
// für die Middleware-Tests liegen in derselben Assembly; die Tests hier schalten sie ab.

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
    [Function("McpTools")]
    public string Tools() => "tools";
}

// [RequireAnyRole] und [AllowWithoutRole] versprechen eine Anmeldung; ohne Token-Prüfung wären sie anonym.
public sealed class CoreUsageRoleFunctions
{
    [Function("ListOrders")]
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
        DisableOtherFunctions(builder);
        builder.AddSeedCore();

        var error = Assert.Throws<OptionsValidationException>(() => Resolve(builder));

        Assert.Contains("Functions ListInvoices, McpTools, ListOrders, WhoAmI verlangen", error.Message);
        Assert.DoesNotContain("Ping", error.Message);
    }

    [Fact]
    public void WithoutUseSeedAuth_DisabledFunctions_DoNotCount()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        DisableOtherFunctions(builder);
        builder.Configuration["AzureWebJobs.ListInvoices.Disabled"] = "true";
        builder.Configuration["AzureWebJobs.McpTools.Disabled"] = "1";
        builder.Configuration["AzureWebJobs.ListOrders.Disabled"] = "true";
        builder.Configuration["AzureWebJobs.WhoAmI.Disabled"] = "true";
        builder.AddSeedCore();

        Assert.NotNull(Resolve(builder));
    }

    [Fact]
    public void WithoutUseSeedAuth_FunctionDisabledWithFalse_StillCounts()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        DisableOtherFunctions(builder);
        builder.Configuration["AzureWebJobs.ListInvoices.Disabled"] = "false";
        builder.Configuration["AzureWebJobs.McpTools.Disabled"] = "true";
        builder.Configuration["AzureWebJobs.ListOrders.Disabled"] = "true";
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

    /// <summary>Schaltet die Functions dieser Assembly ab, die nicht zu diesen Tests gehören.</summary>
    private static void DisableOtherFunctions(FunctionsApplicationBuilder builder)
    {
        Type[] own = [typeof(CoreUsageFunctions), typeof(CoreUsageScopedFunctions), typeof(CoreUsageRoleFunctions)];
        var others = typeof(SeedCoreUsageTests).Assembly.GetTypes()
            .Where(t => !own.Contains(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<FunctionAttribute>()?.Name)
            .OfType<string>();
        foreach (var name in others)
        {
            builder.Configuration[$"AzureWebJobs.{name}.Disabled"] = "true";
        }
    }

    private static SeedOptions Resolve(FunctionsApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SeedOptions>>().Value;
    }
}
