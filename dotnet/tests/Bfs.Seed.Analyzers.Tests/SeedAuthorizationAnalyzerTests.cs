using Microsoft.CodeAnalysis;

namespace Bfs.Seed.Analyzers.Tests;

public class SeedAuthorizationAnalyzerTests
{
    private const string Usings = """
        using Bfs.Seed.Auth;
        using Microsoft.AspNetCore.Authorization;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Azure.Functions.Worker;
        using ModelContextProtocol.Server;

        """;

    [Fact]
    public async Task HttpFunction_WithoutMarker_IsError()
    {
        var diagnostics = await RunAsync("""
            public sealed class OrderFunctions
            {
                [Function("GetOrder")]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id}")] HttpRequest request, string id) => new OkResult();
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDAUTH001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'GetOrder'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("Get", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Theory]
    [InlineData("[RequireCapability(\"orders.read\")]", "")]
    [InlineData("", "[RequireCapability(\"orders.read\")]")]
    [InlineData("[RequireAnyRole]", "")]
    [InlineData("", "[RequireAnyRole]")]
    [InlineData("[AllowWithoutRole]", "")]
    [InlineData("", "[AllowWithoutRole]")]
    [InlineData("[AllowAnonymous]", "")]
    [InlineData("", "[AllowAnonymous]")]
    public async Task HttpFunction_WithMarkerOnMethodOrClass_IsFine(string onMethod, string onClass)
    {
        var diagnostics = await RunAsync($$"""
            {{onClass}}
            public sealed class OrderFunctions
            {
                [Function("GetOrder")]
                {{onMethod}}
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HttpFunction_MarkerOnBaseClass_Counts()
    {
        // Wie die Middleware (GetCustomAttributes(inherit: true)): Inherited = true gilt auch für die Basisklasse.
        var diagnostics = await RunAsync("""
            [RequireAnyRole]
            public abstract class ProtectedFunctions;

            public sealed class OrderFunctions : ProtectedFunctions
            {
                [Function("GetOrder")]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HttpFunction_RequireScopeAlone_IsError()
    {
        // Ein Scope sagt, welche Anwendung im Namen der Person aufruft, nicht, wer die Person sein darf.
        var diagnostics = await RunAsync("""
            [RequireScope("mcp_access")]
            public sealed class ToolFunctions
            {
                [Function("Tools")]
                public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.Equal("SEEDAUTH001", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task FunctionWithoutHttpTrigger_IsIgnored()
    {
        var diagnostics = await RunAsync("""
            public sealed class Jobs
            {
                [Function("ProcessOrder")]
                public void Process([Trigger] string message) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class TriggerAttribute : System.Attribute;
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MethodWithoutFunctionAttribute_IsIgnored()
    {
        var diagnostics = await RunAsync("""
            public sealed class Helpers
            {
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task WithoutSeedAuth_NothingIsReported()
    {
        // Ohne Bfs.Seed.Auth prüft niemand die Attribute; der Analyzer kommt ohnehin nur mit dem Paket.
        var diagnostics = await AnalyzerRunner.RunAsync(new SeedAuthorizationAnalyzer(), """
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Azure.Functions.Worker;

            public sealed class OrderFunctions
            {
                [Function("GetOrder")]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """, "Bfs.Seed.Auth", "Bfs.Seed.Mcp");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task McpTool_WithoutMarker_IsError()
    {
        var diagnostics = await RunAsync("""
            [McpServerToolType]
            public sealed class OrderTools
            {
                [McpServerTool(Name = "auftrag_abrufen", ReadOnly = true)]
                public string GetOrder(string id) => id;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDAUTH002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'auftrag_abrufen'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[RequireCapability(\"orders.read\")]", "")]
    [InlineData("", "[RequireCapability(\"orders.read\")]")]
    [InlineData("[RequireAnyRole]", "")]
    [InlineData("", "[AllowWithoutRole]")]
    public async Task McpTool_WithMarker_IsFine(string onMethod, string onClass)
    {
        var diagnostics = await RunAsync($$"""
            [McpServerToolType]
            {{onClass}}
            public sealed class OrderTools
            {
                [McpServerTool(Name = "auftrag_abrufen")]
                {{onMethod}}
                public string GetOrder(string id) => id;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task McpTool_AllowAnonymous_DoesNotCount()
    {
        // Der MCP-Endpunkt verlangt immer ein Token; ein „anonymes“ Werkzeug gibt es nicht.
        var diagnostics = await RunAsync("""
            [McpServerToolType]
            [AllowAnonymous]
            public sealed class OrderTools
            {
                [McpServerTool]
                public string GetOrder(string id) => id;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDAUTH002", diagnostic.Id);
        Assert.Contains("'GetOrder'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpTool_WithoutSeedMcp_IsIgnored()
    {
        // Ein eigener MCP-Server mit dem SDK kennt die Seed-Attribute nicht.
        var diagnostics = await AnalyzerRunner.RunAsync(new SeedAuthorizationAnalyzer(), """
            using ModelContextProtocol.Server;

            [McpServerToolType]
            public sealed class OrderTools
            {
                [McpServerTool]
                public string GetOrder(string id) => id;
            }
            """, "Bfs.Seed.Mcp");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Suppression_WithJustification_IsHonored()
    {
        var diagnostics = await RunAsync("""
            public sealed class OrderFunctions
            {
                [Function("GetOrder")]
                [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "SEEDAUTH001", Justification = "Prüft die Signatur des Webhooks selbst.")]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.True(Assert.Single(diagnostics).IsSuppressed);
    }

    private static Task<IReadOnlyList<Diagnostic>> RunAsync(string source) =>
        AnalyzerRunner.RunAsync(new SeedAuthorizationAnalyzer(), Usings + source);
}
