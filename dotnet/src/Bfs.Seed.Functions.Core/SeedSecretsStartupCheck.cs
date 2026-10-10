using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bfs.Seed.Functions.Core;

/// <summary>
/// Meldet beim Start jedes App-Setting mit Platzhalter oder nicht aufgelöster Key-Vault-Referenz
/// im Log. Die App startet trotzdem: Beim ersten Deploy hat noch niemand die Werte gesetzt, und
/// der Smoke-Test der Pipeline braucht den Health-Endpunkt. Der zeigt dieselben Funde.
/// </summary>
internal sealed partial class SeedSecretsStartupCheck(SeedSecrets secrets, ILogger<SeedSecretsStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var problem in secrets.Problems)
        {
            if (problem.State == SeedSecretState.Placeholder)
            {
                LogPlaceholder(logger, problem.Setting, problem.Secret ?? "?");
            }
            else
            {
                LogUnresolved(logger, problem.Setting, problem.Secret ?? "?");
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Secret {Secret} hat noch den Platzhalter (App-Setting {Setting}). Wert im Key Vault setzen und die Referenzen neu laden.")]
    private static partial void LogPlaceholder(ILogger logger, string setting, string secret);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Key-Vault-Referenz im App-Setting {Setting} (Secret {Secret}) ist nicht aufgelöst. Secret im Vault und Rolle Key Vault Secrets User der Function prüfen, dann die Referenzen neu laden.")]
    private static partial void LogUnresolved(ILogger logger, string setting, string secret);
}

internal static class SeedSecretsServiceCollectionExtensions
{
    public static IServiceCollection AddSeedSecrets(this IServiceCollection services)
    {
        services.AddSingleton<SeedSecrets>();
        services.AddHostedService<SeedSecretsStartupCheck>();
        return services;
    }
}
