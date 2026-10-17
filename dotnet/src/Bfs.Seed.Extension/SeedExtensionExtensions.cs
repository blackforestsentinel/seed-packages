using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bfs.Seed.Extension;

/// <summary>Registrierung des Update-Endpunkts.</summary>
public static class SeedExtensionExtensions
{
    /// <summary>
    /// Schaltet <c>/api/extension/updates.xml</c> und <c>/api/extension/packages/{file}</c> ein. Die
    /// Pakete liegen im Container <c>extension</c> des Projekt-Storage; das setzt das Feature storage
    /// voraus (<c>Seed:Features:Storage</c> und <c>AddSeedStorage()</c>).
    /// </summary>
    public static FunctionsApplicationBuilder AddSeedExtensionUpdates(this FunctionsApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register(builder.Services, builder.Configuration);
        return builder;
    }

    internal static void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Seed:Features:Storage"))
        {
            throw new InvalidOperationException(
                "AddSeedExtensionUpdates(): Die Pakete liegen im Projekt-Storage. In project.yaml features.storage einschalten und den Container extension unter storage.containers eintragen; lokal Seed__Features__Storage=true setzen.");
        }

        services
            .AddOptions<SeedExtensionOptions>()
            .Bind(configuration.GetSection(SeedExtensionOptions.SectionName))
            .PostConfigure(o => o.PackageName ??= configuration["Seed:Project"])
            .Validate(o => SeedExtension.IsValidPackageName(o.PackageName),
                "Extension:PackageName (Standard Seed:Project) muss aus Kleinbuchstaben, Ziffern und Bindestrichen bestehen, wie extension.name in azure-pipelines.yml.")
            .Validate(o => o.CacheDuration >= TimeSpan.Zero && o.CacheDuration <= TimeSpan.FromHours(1),
                "Extension:CacheDuration muss zwischen 0 und einer Stunde liegen.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISeedExtensionReleases, SeedBlobExtensionReleases>();
    }
}
