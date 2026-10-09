using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Functions.Core.Tests;

public class SeedCoreExtensionsTests
{
    [Fact]
    public void AddSeedCore_BindsSeedOptionsFromConfiguration()
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        builder.Configuration["Seed:Project"] = "kundenportal";
        builder.Configuration["Seed:Environment"] = "dev";

        builder.AddSeedCore();

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SeedOptions>>().Value;
        Assert.Equal("kundenportal", options.Project);
        Assert.Equal("dev", options.Environment);
    }

    [Fact]
    public void RemoveApplicationInsightsDefaultRule_RemovesOnlyThatRule()
    {
        var options = new LoggerFilterOptions();
        options.Rules.Add(new LoggerFilterRule(SeedCoreExtensions.ApplicationInsightsLoggerProvider, null, LogLevel.Warning, null));
        options.Rules.Add(new LoggerFilterRule("Other", null, LogLevel.Error, null));

        SeedCoreExtensions.RemoveApplicationInsightsDefaultRule(options);

        var remaining = Assert.Single(options.Rules);
        Assert.Equal("Other", remaining.ProviderName);
    }

    [Fact]
    public void SeedHealthReport_UsesOptions()
    {
        var report = SeedHealthReport.Create(
            new SeedOptions { Project = "kundenportal", Environment = "prod" },
            typeof(SeedCoreExtensionsTests).Assembly);

        Assert.Equal(("ok", "kundenportal", "prod"), (report.Status, report.Project, report.Environment));
    }
}
