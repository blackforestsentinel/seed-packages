using System.Security.Claims;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Auth.Tests;

public sealed class SeedAuthExtensionsTests : IDisposable
{
    private readonly string projectFile = Path.Combine(Path.GetTempPath(), $"seed-auth-{Guid.NewGuid()}.yaml");

    public SeedAuthExtensionsTests() => File.WriteAllText(projectFile, SeedCapabilityMapTests.ProjectYaml);

    public void Dispose() => File.Delete(projectFile);

    [Fact]
    public void EntraMode_WithoutTenant_DoesNotStart()
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(new() { ["Auth:ClientId"] = "api" }));

        Assert.Contains("Auth:TenantId und Auth:ClientId fehlen", error.Message);
    }

    [Fact]
    public void ValidConfiguration_LoadsCapabilityMapFromProjectFile()
    {
        using var provider = Build(Entra());

        Assert.Equal(TestTokens.TenantId, provider.GetRequiredService<IOptions<SeedAuthOptions>>().Value.TenantId);
        Assert.Equal(["Admin", "Reader", "Sync"], provider.GetRequiredService<SeedCapabilityMap>().Roles.Keys.Order());
        Assert.NotNull(provider.GetRequiredService<SeedTokenValidator>());
    }

    [Fact]
    public void UnknownCapability_DoesNotStart()
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(Entra(), typeof(TypoFunctions).Assembly));

        Assert.Contains($"{typeof(TypoFunctions).FullName}: invoices.wirte", error.Message);
        Assert.Contains($"{nameof(CapabilityFunctions.Delete)}: invoices.delete", error.Message);
    }

    [Fact]
    public void UnknownCapability_WithoutProjectFile_NamesTheMissingFile()
    {
        var settings = Entra();
        settings["Auth:ProjectFile"] = Path.Combine(Path.GetTempPath(), "fehlt.yaml");

        var error = Assert.Throws<OptionsValidationException>(() => Resolve(settings, typeof(TypoFunctions).Assembly));

        Assert.Contains("fehlt.yaml (nicht gefunden)", error.Message);
    }

    [Fact]
    public void LocalMode_StartsWithoutEntraSettings()
    {
        using var provider = Build(new()
        {
            ["Auth:Mode"] = "Local",
            ["Auth:ProjectFile"] = projectFile,
            ["Auth:LocalUser:Roles:0"] = "Admin",
        });

        var options = provider.GetRequiredService<IOptions<SeedAuthOptions>>().Value;
        var user = SeedLocalPrincipal.Create(options, provider.GetRequiredService<SeedCapabilityMap>());
        Assert.Equal(SeedAuthMode.Local, options.Mode);
        Assert.Equal(["invoices.read", "invoices.write"], user.GetCapabilities().Order());
    }

    [Fact]
    public void LocalMode_InAzure_DoesNotStart()
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Auth:Mode"] = "Local",
            ["Auth:ProjectFile"] = projectFile,
            ["WEBSITE_INSTANCE_ID"] = "abc123",
        }));

        Assert.Contains("nur lokal erlaubt", error.Message);
    }

    [Fact]
    public void LocalMode_WithUnknownRole_DoesNotStart()
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Auth:Mode"] = "Local",
            ["Auth:ProjectFile"] = projectFile,
            ["Auth:LocalUser:Roles:0"] = "Admn",
        }));

        Assert.Contains("Rolle Admn", error.Message);
    }

    [Fact]
    public void InvalidProjectFile_DoesNotStart()
    {
        File.WriteAllText(projectFile, "auth:\n  roles:\n    Admin:\n      memberTypes: [Service]");

        var error = Assert.Throws<OptionsValidationException>(() => Resolve(Entra()));

        Assert.Contains("„Service“ ist unbekannt", error.Message);
    }

    [Fact]
    public void SeedUserInfo_ListsRolesAndCapabilities()
    {
        var options = new SeedAuthOptions { LocalUser = { Roles = ["Reader", "Admin"] } };

        var info = SeedUserInfo.From(SeedLocalPrincipal.Create(options, SeedCapabilityMap.Parse(SeedCapabilityMapTests.ProjectYaml)));

        Assert.Equal(("Lokale Entwicklung", "dev@localhost"), (info.Name, info.Username));
        Assert.Equal(["Admin", "Reader"], info.Roles);
        Assert.Equal(["invoices.read", "invoices.write"], info.Capabilities);
        Assert.Empty(SeedUserInfo.From(new ClaimsPrincipal()).Capabilities);
    }

    private Dictionary<string, string?> Entra() => new()
    {
        ["Auth:TenantId"] = TestTokens.TenantId,
        ["Auth:ClientId"] = TestTokens.ClientId,
        ["Auth:ProjectFile"] = projectFile,
    };

    private static void Resolve(Dictionary<string, string?> settings, params System.Reflection.Assembly[] assemblies)
    {
        using var provider = Build(settings, assemblies);
        _ = provider.GetRequiredService<IOptions<SeedAuthOptions>>().Value;
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, params System.Reflection.Assembly[] assemblies)
    {
        var builder = FunctionsApplication.CreateBuilder([]);
        foreach (var (key, value) in settings)
        {
            builder.Configuration[key] = value;
        }

        // Ohne Angabe prüft UseSeedAuth() die Einstiegs-Assembly; hier wäre das der Testlauf
        // mit den absichtlich falschen Capabilities aus TestFunctions.
        builder.Services.AddSingleton(new SeedAuthAssemblies(assemblies));
        builder.UseSeedAuth();
        return builder.Services.BuildServiceProvider();
    }
}
