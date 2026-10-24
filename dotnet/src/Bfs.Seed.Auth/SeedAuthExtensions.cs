using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// startet die App nicht, ebenso wenn ein <c>[RequireCapability]</c> eine Capability nennt,
    /// die <c>auth.roles</c> in project.yaml nicht vergibt, oder <c>Auth:AllowedClientIds</c> einen
    /// Eintrag hat, der keine GUID ist.
    /// </summary>
    public static FunctionsApplicationBuilder UseSeedAuth(this FunctionsApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<SeedAuthOptions>()
            .Bind(builder.Configuration.GetSection(SeedAuthOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SeedAuthOptions>, SeedAuthOptionsValidation>();

        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SeedAuthOptions>>().Value;
            return SeedCapabilityMap.Load(ProjectFilePath(options));
        });
        builder.Services.TryAddSingleton(new SeedAuthAssemblies([.. new[] { Assembly.GetEntryAssembly() }.OfType<Assembly>()]));

        builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SeedAuthOptions>>().Value;
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{options.Authority}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
        });
        builder.Services.AddSingleton(sp => new SeedTokenValidator(
            sp.GetRequiredService<IOptions<SeedAuthOptions>>(),
            sp.GetRequiredService<IConfigurationManager<OpenIdConnectConfiguration>>(),
            sp.GetRequiredService<SeedCapabilityMap>()));

        builder.UseMiddleware<SeedAuthMiddleware>();
        return builder;
    }

    internal static string ProjectFilePath(SeedAuthOptions options) =>
        Path.Combine(AppContext.BaseDirectory, options.ProjectFile);
}

/// <summary>Assemblies, deren <c>[RequireCapability]</c> beim Start geprüft werden; Standard ist die App.</summary>
internal sealed record SeedAuthAssemblies(IReadOnlyList<Assembly> Assemblies);

/// <summary>Prüfungen beim Start: fehlende Einstellungen, lokaler Modus in Azure, Tippfehler in Client-IDs und Capabilities.</summary>
internal sealed class SeedAuthOptionsValidation(IConfiguration configuration, IServiceProvider services)
    : IValidateOptions<SeedAuthOptions>
{
    // Variablen, die nur die Azure-Plattform setzt. Jeder Hosting-Plan setzt nur einen Teil davon:
    // WEBSITE_INSTANCE_ID fehlt etwa auf Linux Consumption und Flex Consumption, dort gibt es
    // WEBSITE_SITE_NAME, WEBSITE_SKU und CONTAINER_NAME bzw. WEBSITE_POD_NAME. Container Apps setzt
    // CONTAINER_APP_NAME. Die Core Tools (func start) setzen keine davon.
    internal static readonly string[] AzureHostingVariables =
        ["WEBSITE_INSTANCE_ID", "WEBSITE_SITE_NAME", "WEBSITE_SKU", "WEBSITE_POD_NAME", "CONTAINER_NAME", "CONTAINER_APP_NAME"];

    public ValidateOptionsResult Validate(string? name, SeedAuthOptions options)
    {
        var failures = new List<string>();

        if (options.Mode == SeedAuthMode.Entra
            && (string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.ClientId)))
        {
            failures.Add("UseSeedAuth(): Auth:TenantId und Auth:ClientId fehlen. Ist das Feature sso in project.yaml aktiv?");
        }

        // In Azure darf es keinen Nutzer ohne Token geben.
        if (options.Mode == SeedAuthMode.Local
            && AzureHostingVariables.FirstOrDefault(v => !string.IsNullOrEmpty(configuration[v])) is { } variable)
        {
            failures.Add($"UseSeedAuth(): Auth:Mode=Local ist nur lokal erlaubt, die App läuft aber in Azure ({variable} ist gesetzt).");
        }

        // Ein Tippfehler sperrte sonst still die Anwendung aus, die gemeint war.
        if (options.AllowedClientIds.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && !Guid.TryParse(c, out _)) is { } client)
        {
            failures.Add($"UseSeedAuth(): Auth:AllowedClientIds nennt „{client}“, das ist keine Client-ID (GUID).");
        }

        SeedCapabilityMap map;
        try
        {
            map = SeedCapabilityMap.Load(SeedAuthExtensions.ProjectFilePath(options));
        }
        catch (InvalidOperationException e)
        {
            return ValidateOptionsResult.Fail([.. failures, e.Message]);
        }

        var assemblies = services.GetService(typeof(SeedAuthAssemblies)) as SeedAuthAssemblies;
        var unknown = SeedCapabilityCheck.FindUnknownCapabilities(map, [.. assemblies?.Assemblies ?? []]);
        if (unknown.Count > 0)
        {
            var where = map.Source ?? $"{SeedAuthExtensions.ProjectFilePath(options)} (nicht gefunden)";
            failures.Add($"UseSeedAuth(): [RequireCapability] nennt Capabilities, die keine Rolle in auth.roles von {where} vergibt: {string.Join(", ", unknown)}");
        }

        if (options.Mode == SeedAuthMode.Local && options.LocalUser.Roles.FirstOrDefault(r => !map.Roles.ContainsKey(r)) is { } role)
        {
            failures.Add($"UseSeedAuth(): Auth:LocalUser:Roles nennt die Rolle {role}, die auth.roles in project.yaml nicht kennt.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
