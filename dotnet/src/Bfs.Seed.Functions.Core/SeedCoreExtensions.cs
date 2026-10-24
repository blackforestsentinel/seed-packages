using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Functions.Core;

/// <summary>Registrierung der Seed-Grundausstattung.</summary>
public static class SeedCoreExtensions
{
    internal const string ApplicationInsightsLoggerProvider =
        "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider";

    /// <summary>
    /// Registriert Application Insights für den Worker, bindet <see cref="SeedOptions"/>
    /// an den Abschnitt <c>Seed</c> und registriert <see cref="SeedSecrets"/>. Die App startet
    /// nicht, wenn eine Function <c>[RequireCapability]</c> oder <c>[RequireScope]</c> trägt und
    /// <c>UseSeedAuth()</c> fehlt.
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
            .Bind(builder.Configuration.GetSection(SeedOptions.SectionName))
            .ValidateOnStart();

        // Functions mit [RequireCapability] oder [RequireScope] nur mit Token-Prüfung. Geprüft wird
        // beim Start, wenn Program.cs UseSeedAuth() schon aufgerufen hat oder eben nicht.
        var services = builder.Services;
        builder.Services.TryAddSingleton(new SeedFunctionAssemblies([.. new[] { Assembly.GetEntryAssembly() }.OfType<Assembly>()]));
        builder.Services.AddSingleton<IValidateOptions<SeedOptions>>(sp => new SeedAuthUsageValidation(
            services, sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<SeedFunctionAssemblies>()));

        // SeedSecrets und die Prüfung beim Start auf Platzhalter und nicht aufgelöste Referenzen.
        builder.Services.AddSeedSecrets();

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
