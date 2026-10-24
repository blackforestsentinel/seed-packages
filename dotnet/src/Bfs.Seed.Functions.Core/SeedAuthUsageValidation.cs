using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Functions.Core;

/// <summary>Assemblies, deren Functions beim Start geprüft werden; Standard ist die App.</summary>
internal sealed record SeedFunctionAssemblies(IReadOnlyList<Assembly> Assemblies);

/// <summary>
/// Lässt die App nicht starten, wenn eine aufrufbare Function <c>[RequireCapability]</c> oder
/// <c>[RequireScope]</c> aus Bfs.Seed.Auth trägt, die Token-Prüfung (<c>UseSeedAuth()</c>) aber
/// fehlt. Die Attribute prüft nur deren Middleware; ohne sie wäre die Function für jeden ohne
/// Anmeldung erreichbar. Abgeschaltete Functions (<c>AzureWebJobs.&lt;Name&gt;.Disabled</c>) zählen nicht.
/// </summary>
internal sealed class SeedAuthUsageValidation(IServiceCollection services, IConfiguration configuration, SeedFunctionAssemblies assemblies)
    : IValidateOptions<SeedOptions>
{
    // Namen statt Typen: Bfs.Seed.Functions.Core referenziert Bfs.Seed.Auth nicht.
    internal static readonly string[] AuthAttributes =
        ["Bfs.Seed.Auth.RequireCapabilityAttribute", "Bfs.Seed.Auth.RequireScopeAttribute"];

    // Registriert nur UseSeedAuth(), in allen Versionen von Bfs.Seed.Auth.
    internal const string AuthService = "Bfs.Seed.Auth.SeedTokenValidator";

    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        if (services.Any(d => d.ServiceType.FullName == AuthService))
        {
            return ValidateOptionsResult.Success;
        }

        var open = FindProtectedFunctions(configuration, assemblies.Assemblies);
        return open.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"AddSeedCore(): Die Functions {string.Join(", ", open)} verlangen mit [RequireCapability] oder [RequireScope] eine Berechtigung, " +
                "aber die Token-Prüfung fehlt (UseSeedAuth(), Feature sso). Ohne sie wären sie für jeden ohne Anmeldung erreichbar. " +
                "sso einschalten oder die Functions abschalten (App-Setting AzureWebJobs.<Name>.Disabled=true).");
    }

    /// <summary>Namen der nicht abgeschalteten Functions, die eine Capability oder einen Scope verlangen.</summary>
    internal static IReadOnlyList<string> FindProtectedFunctions(IConfiguration configuration, IEnumerable<Assembly> assemblies)
    {
        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var found = new List<string>();

        foreach (var type in assemblies.SelectMany(LoadableTypes))
        {
            foreach (var method in type.GetMethods(Members))
            {
                if (method.GetCustomAttribute<FunctionAttribute>() is { } function
                    && !IsDisabled(configuration, function.Name)
                    && (HasAuthAttribute(method) || HasAuthAttribute(type)))
                {
                    found.Add(function.Name);
                }
            }
        }

        return found;
    }

    private static bool HasAuthAttribute(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(a => AuthAttributes.Contains(a.GetType().FullName));

    // Wie der Functions-Host: true oder 1 schaltet die Function ab.
    private static bool IsDisabled(IConfiguration configuration, string function) =>
        configuration[$"AzureWebJobs.{function}.Disabled"] is { } value
        && (bool.TryParse(value, out var disabled) ? disabled : value.Trim() == "1");

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }
}
