using Bfs.Seed.Auth;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bfs.Seed.Mcp;

/// <summary>Registrierung des MCP-Servers.</summary>
public static class SeedMcpExtensions
{
    /// <summary>
    /// Schaltet den MCP-Endpunkt <c>/api/mcp</c> und seine Metadaten nach RFC 9728 ein. Werkzeuge
    /// kommen über <see cref="SeedMcpBuilder"/> dazu. Setzt das Feature sso voraus
    /// (<c>Seed:Features:Sso</c> und <c>UseSeedAuth()</c>); ohne sso startet die App nicht.
    /// </summary>
    public static SeedMcpBuilder AddSeedMcp(this FunctionsApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return Register(builder.Services, builder.Configuration);
    }

    internal static SeedMcpBuilder Register(IServiceCollection services, IConfiguration configuration)
    {
        // Ohne Token-Prüfung stünde der Endpunkt offen, und Werkzeuge ohne Capability könnte jeder aufrufen.
        if (!configuration.GetValue<bool>("Seed:Features:Sso"))
        {
            throw new InvalidOperationException(
                "AddSeedMcp(): Das Feature mcp setzt sso voraus. In project.yaml features.sso einschalten; lokal Seed__Features__Sso=true setzen.");
        }

        services
            .AddOptions<SeedMcpOptions>()
            .Bind(configuration.GetSection(SeedMcpOptions.SectionName))
            .PostConfigure(o => o.ServerName ??= configuration["Seed:Project"])
            .Validate(o => SeedMcpResource.IsValidConfiguredResource(o.Resource),
                $"Mcp:Resource muss eine https-Adresse ohne Query sein, die auf {SeedMcp.Path} endet, z. B. https://mcp.example.org{SeedMcp.Path}.")
            .ValidateOnStart();

        // Damit nennt jede 401-Antwort der Token-Prüfung die Metadaten als resource_metadata.
        services.PostConfigure<SeedAuthOptions>(o => o.ResourceMetadataPath ??= SeedMcp.ResourceMetadataPath);

        services.TryAddSingleton<SeedMcpToolCatalog>();
        services.TryAddSingleton<SeedMcpEndpoint>();
        return new SeedMcpBuilder(services);
    }
}
