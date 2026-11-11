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

    [Fact]
    public async Task Rules_IgnorePragmaNoWarnAndEditorConfig()
    {
        // NoWarn und dotnet_diagnostic.<ID>.severity landen beide in diesen Optionen. Auch ein
        // Pragma mit Begründung schaltet die Regel nicht ab, nur [SuppressMessage] mit Begründung.
        var diagnostics = await AnalyzerRunner.RunAsync(
            new SeedAuthorizationAnalyzer(),
            [Usings + """
            public sealed class OrderFunctions
            {
            #pragma warning disable SEEDAUTH001 // Webhook prüft die Signatur selbst.
                [Function("GetOrder")]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            #pragma warning restore SEEDAUTH001
            }
            """],
            new Dictionary<string, ReportDiagnostic>
            {
                ["SEEDAUTH001"] = ReportDiagnostic.Suppress,
                ["SEEDAUTH002"] = ReportDiagnostic.Suppress,
                ["SEEDAUTH003"] = ReportDiagnostic.Suppress,
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDAUTH001", diagnostic.Id);
        Assert.False(diagnostic.IsSuppressed);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("Test0.cs", "// <auto-generated/>\n")]
    [InlineData("Orders.g.cs", "")]
    public async Task GeneratedCode_IsCheckedToo(string path, string header)
    {
        // Sonst ließe sich eine offene Function per Kopfzeile oder Dateiname vorbeischleusen.
        var diagnostics = await AnalyzerRunner.RunAsync(new SeedAuthorizationAnalyzer(), [(path, header + Usings + """
            public sealed class OrderFunctions
            {
                [Function("GetOrder")]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }

            [McpServerToolType]
            public sealed class OrderTools
            {
                [McpServerTool]
                public string GetOrder(string id) => id;
            }
            """)]);

        Assert.Equal(["SEEDAUTH001", "SEEDAUTH002"], diagnostics.Effective().Select(d => d.Id));
    }

    [Theory]
    [InlineData("[AllowAnonymous]", "", "[RequireCapability(\"orders.read\")]", "RequireCapability")]
    [InlineData("[RequireAnyRole]", "", "[AllowAnonymous]", "RequireAnyRole")]
    [InlineData("", "[AllowAnonymous]", "[AllowAnonymous][AllowWithoutRole]", "AllowWithoutRole")]
    [InlineData("[AllowAnonymous]", "", "[RequireScope(\"mcp_access\")][RequireCapability(\"orders.read\")]", "RequireCapability], [RequireScope")]
    public async Task AllowAnonymous_WithOtherAttribute_IsError(string onClass, string onOtherMethod, string onMethod, string overridden)
    {
        // Typischer Fall: [AllowAnonymous] an der Klasse wegen Health, [RequireCapability] an einer
        // anderen Methode. Die Middleware fragt IAllowAnonymous zuerst ab, die Methode wäre anonym.
        var diagnostics = await RunAsync($$"""
            {{onClass}}
            public sealed class OrderFunctions
            {
                [Function("Health")]
                {{onOtherMethod}}
                public IActionResult Health([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();

                [Function("GetOrder")]
                {{onMethod}}
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.GetMessage().Contains("'GetOrder'", StringComparison.Ordinal));
        Assert.Equal("SEEDAUTH003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"[{overridden}] prüft die API deshalb nicht", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowAnonymous_OnBaseClass_CountsForSeedAuth003()
    {
        var diagnostics = await RunAsync("""
            [AllowAnonymous]
            public abstract class PublicFunctions;

            public sealed class OrderFunctions : PublicFunctions
            {
                [Function("GetOrder")]
                [RequireAnyRole]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.Equal("SEEDAUTH003", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task AllowAnonymous_OnSeparateFunctions_IsFine()
    {
        // Wie in Bfs.Seed.Mcp: [AllowAnonymous] nur an der Methode, die es braucht.
        var diagnostics = await RunAsync("""
            public sealed class McpFunctions
            {
                [Function("Mcp")]
                [RequireScope("mcp_access")]
                [AllowWithoutRole]
                public IActionResult Mcp([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest request) => new OkResult();

                [Function("Metadata")]
                [AllowAnonymous]
                public IActionResult Metadata([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task McpTool_AllowAnonymousWithCapability_IsNoContradiction()
    {
        // Bfs.Seed.Mcp wertet [AllowAnonymous] an Werkzeugen nicht aus; die Capability gilt.
        var diagnostics = await RunAsync("""
            [McpServerToolType]
            [AllowAnonymous]
            public sealed class OrderTools
            {
                [McpServerTool]
                [RequireCapability("orders.read")]
                public string GetOrder(string id) => id;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Functions_WithSameMethodName_AreErrors()
    {
        // Beide Functions haben den Entry Point OrderFunctions.Run, gleich welcher Trigger.
        var diagnostics = await RunAsync("""
            public sealed class OrderFunctions
            {
                [Function("DeleteOrder")]
                [RequireCapability("orders.delete")]
                public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "delete")] HttpRequest request) => new OkResult();

                [Function("ProcessOrder")]
                public void Run([Trigger] string message) { }

                [Function("GetOrder")]
                [RequireCapability("orders.read")]
                public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }

            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class TriggerAttribute : System.Attribute;
            """);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("SEEDAUTH004", d.Id);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal(
                "Die Functions 'DeleteOrder', 'ProcessOrder' in 'OrderFunctions' teilen sich den Methodennamen 'Run'. Gib jeder Function eine Methode mit eigenem Namen.",
                d.GetMessage());
            Assert.Equal("Run", d.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(d.Location.SourceSpan));
        });
    }

    [Fact]
    public async Task Functions_WithSameMethodNameInDifferentClasses_AreFine()
    {
        var diagnostics = await RunAsync("""
            public sealed class OrderFunctions
            {
                [Function("GetOrder")]
                [RequireCapability("orders.read")]
                public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();

                // Eine Überladung ohne [Function] ist keine zweite Function.
                public IActionResult Run(string id) => new OkResult();
            }

            public sealed class InvoiceFunctions
            {
                [Function("GetInvoice")]
                [RequireCapability("invoices.read")]
                public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Rules_AreErrorsAndCannotBeConfiguredAway()
    {
        var analyzer = new SeedAuthorizationAnalyzer();

        Assert.Equal(["SEEDAUTH001", "SEEDAUTH002", "SEEDAUTH003", "SEEDAUTH004"], analyzer.SupportedDiagnostics.Select(d => d.Id));
        Assert.All(analyzer.SupportedDiagnostics, d =>
        {
            Assert.Equal(DiagnosticSeverity.Error, d.DefaultSeverity);
            Assert.True(d.IsEnabledByDefault);
            Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, d.CustomTags);
            Assert.EndsWith(d.Id.ToLowerInvariant(), d.HelpLinkUri, StringComparison.Ordinal);
        });
    }

    private static Task<IReadOnlyList<Diagnostic>> RunAsync(string source) =>
        AnalyzerRunner.RunAsync(new SeedAuthorizationAnalyzer(), Usings + source);
}
