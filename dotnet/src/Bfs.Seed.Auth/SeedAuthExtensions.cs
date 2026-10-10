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
    /// die <c>auth.roles</c> in project.yaml nicht vergibt.
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

/// <summary>Prüfungen beim Start: fehlende Einstellungen, lokaler Modus in Azure, Tippfehler in Capabilities.</summary>
internal sealed class SeedAuthOptionsValidation(IConfiguration configuration, IServiceProvider services)
    : IValidateOptions<SeedAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, SeedAuthOptions options)
    {
        var failures = new List<string>();

        if (options.Mode == SeedAuthMode.Entra
            && (string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.ClientId)))
        {
            failures.Add("UseSeedAuth(): Auth:TenantId und Auth:ClientId fehlen. Ist das Feature sso in project.yaml aktiv?");
        }

        // In Azure setzt die Plattform WEBSITE_INSTANCE_ID; dort darf es keinen Nutzer ohne Token geben.
        if (options.Mode == SeedAuthMode.Local && !string.IsNullOrEmpty(configuration["WEBSITE_INSTANCE_ID"]))
        {
            failures.Add("UseSeedAuth(): Auth:Mode=Local ist nur lokal erlaubt, die App läuft aber in Azure (WEBSITE_INSTANCE_ID ist gesetzt).");
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
