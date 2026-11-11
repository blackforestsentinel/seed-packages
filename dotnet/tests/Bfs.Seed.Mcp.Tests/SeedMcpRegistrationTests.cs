using System.Reflection;
using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Bfs.Seed.Mcp.Tests;

public class SeedMcpRegistrationTests
{
    [Fact]
    public void WithoutSso_AppDoesNotStart()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:Features:Sso"] = "false" })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => SeedMcpExtensions.Register(new ServiceCollection(), configuration));

        Assert.Contains("setzt sso voraus", error.Message);
    }

    [Fact]
    public void AuthOptions_PointUnauthorizedResponsesToMetadata()
    {
        var app = McpTestApp.Create();

        var options = app.Services.GetRequiredService<IOptions<SeedAuthOptions>>().Value;

        Assert.Equal("/api/.well-known/oauth-protected-resource", options.ResourceMetadataPath);
    }

    [Fact]
    public void InvalidResource_FailsValidation()
    {
        var app = McpTestApp.Create(new() { ["Mcp:Resource"] = "https://mcp.example.org/" });

        var error = Assert.Throws<OptionsValidationException>(() => app.Services.GetRequiredService<IOptions<SeedMcpOptions>>().Value);

        Assert.Contains("Mcp:Resource", error.Message);
    }

    [Fact]
    public void ToolsFromAssembly_FindsAllToolTypes()
    {
        var app = McpTestApp.Create(configure: mcp => mcp.WithToolsFromAssembly(typeof(SampleTools).Assembly));

        var names = app.Services.GetRequiredService<SeedMcpToolCatalog>().All.Select(t => t.Name);

        Assert.Equal(["auftraege", "bericht", "rechnung_anlegen", "status", "wer_bin_ich"], names.Order());
    }

    [Fact]
    public void Capabilities_ComeFromClassAndMethod()
    {
        var catalog = McpTestApp.Create().Services.GetRequiredService<SeedMcpToolCatalog>();

        Assert.Equal(["reports.read", "reports.export"], catalog.Find("bericht")!.RequiredCapabilities);
        Assert.Empty(catalog.Find("status")!.RequiredCapabilities);
    }

    [Fact]
    public void DuplicateToolName_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SeedMcpToolCatalog.Build([typeof(SampleTools), typeof(DuplicateTools)], new ServiceCollection().BuildServiceProvider()));

        Assert.Contains("„status“ ist doppelt", error.Message);
    }

    [Fact]
    public void TypeWithoutTools_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SeedMcpToolCatalog.Build([typeof(Greeter)], new ServiceCollection().BuildServiceProvider()));

        Assert.Contains("[McpServerTool]", error.Message);
    }

    [Fact]
    public void ToolWithoutAuthorization_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SeedMcpToolCatalog.Build([typeof(UnmarkedTools)], new ServiceCollection().BuildServiceProvider()));

        Assert.Contains("„offen“", error.Message);
        Assert.Contains($"{typeof(UnmarkedTools).FullName}.{nameof(UnmarkedTools.Open)}", error.Message);
        Assert.Contains("[RequireCapability(\"…\")], [RequireAnyRole] oder [AllowWithoutRole]", error.Message);
    }

    [Fact]
    public void ToolWithoutAuthorization_StopsTheStart()
    {
        // ValidateOnStart prüft die Optionen beim Start des Hosts; hier dieselbe Auflösung direkt.
        var app = McpTestApp.Create(configure: mcp => mcp.WithTools(typeof(UnmarkedTools)));

        var error = Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IOptions<SeedMcpOptions>>().Value);

        Assert.Contains("„offen“", error.Message);
    }

    [Fact]
    public void ToolWithAuthorizationOnClass_IsAccepted()
    {
        var tools = SeedMcpToolCatalog.Build([typeof(RoleTools)], new ServiceCollection().BuildServiceProvider());

        Assert.True(Assert.Single(tools).RequiresRole);
    }

    [Fact]
    public void Functions_CarryTheAuthContract()
    {
        var mcp = typeof(SeedMcpFunctions).GetMethod(nameof(SeedMcpFunctions.McpAsync))!;
        var metadata = typeof(SeedMcpFunctions).GetMethod(nameof(SeedMcpFunctions.ResourceMetadata))!;

        Assert.Equal([SeedMcp.Scope], mcp.GetCustomAttribute<RequireScopeAttribute>()!.Scopes);
        Assert.Null(mcp.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(metadata.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Functions_WithoutAddSeedMcp_Answer404()
    {
        var functions = new SeedMcpFunctions(new ServiceCollection().BuildServiceProvider());
        var request = McpTestApp.CreateContext("POST", "localhost:7071", McpTestApp.User()).Request;

        Assert.IsType<NotFoundResult>(await functions.McpAsync(request, null!));
        Assert.IsType<NotFoundResult>(functions.ResourceMetadata(request));
    }

    // Ohne [McpServerToolType], damit WithToolsFromAssembly sie nicht aufnimmt.
    public sealed class DuplicateTools
    {
        [McpServerTool(Name = "status")]
        [AllowWithoutRole]
        public static string Status() => "doppelt";
    }

    // Ohne Berechtigungsangabe, etwa aus einer Assembly ohne den Analyzer.
    public sealed class UnmarkedTools
    {
        [McpServerTool(Name = "offen")]
        public static string Open() => "offen";
    }

    [RequireAnyRole]
    public sealed class RoleTools
    {
        [McpServerTool(Name = "mit_rolle")]
        public static string WithRole() => "ok";
    }
}
