using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Bfs.Seed.Auth;

/// <summary>Registrierung der Token-Prüfung.</summary>
public static class SeedAuthExtensions
{
    /// <summary>
    /// Verlangt für alle HTTP-Functions ein gültiges Bearer-Token aus Entra ID. Ausnahmen
    /// markiert <c>[AllowAnonymous]</c>. Ohne <c>Auth:TenantId</c> und <c>Auth:ClientId</c>
    /// startet die App nicht.
    /// </summary>
    public static FunctionsApplicationBuilder UseSeedAuth(this FunctionsApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<SeedAuthOptions>()
            .Bind(builder.Configuration.GetSection(SeedAuthOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.TenantId) && !string.IsNullOrWhiteSpace(o.ClientId),
                "UseSeedAuth(): Auth:TenantId und Auth:ClientId fehlen. Ist das Feature sso in project.yaml aktiv?")
            .ValidateOnStart();

        builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SeedAuthOptions>>().Value;
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{options.Authority}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
        });
        builder.Services.AddSingleton<SeedTokenValidator>();

        builder.UseMiddleware<SeedAuthMiddleware>();
        return builder;
    }
}
