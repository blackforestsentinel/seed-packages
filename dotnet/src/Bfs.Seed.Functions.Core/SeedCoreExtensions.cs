using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bfs.Seed.Functions.Core;

/// <summary>Registrierung der Seed-Grundausstattung.</summary>
public static class SeedCoreExtensions
{
    internal const string ApplicationInsightsLoggerProvider =
        "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider";

    /// <summary>
    /// Registriert Application Insights für den Worker und bindet <see cref="SeedOptions"/>
    /// an den Abschnitt <c>Seed</c>.
    /// </summary>
    public static FunctionsApplicationBuilder AddSeedCore(this FunctionsApplicationBuilder builder)
    {
        builder.Services
            .AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights();

        // Das Application-Insights-SDK filtert per Default alles unter Warning heraus.
        // Ohne diese Regel greifen die Log-Level aus host.json und appsettings.
        builder.Services.Configure<LoggerFilterOptions>(RemoveApplicationInsightsDefaultRule);

        builder.Services
            .AddOptions<SeedOptions>()
            .Bind(builder.Configuration.GetSection(SeedOptions.SectionName));

        return builder;
    }

    internal static void RemoveApplicationInsightsDefaultRule(LoggerFilterOptions options)
    {
        var rule = options.Rules.FirstOrDefault(r => r.ProviderName == ApplicationInsightsLoggerProvider);
        if (rule is not null)
        {
            options.Rules.Remove(rule);
        }
    }
}
