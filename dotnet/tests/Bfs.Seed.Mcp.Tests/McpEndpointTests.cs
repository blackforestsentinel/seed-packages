using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Bfs.Seed.Mcp.Tests;

/// <summary>Der Endpunkt, wie ein MCP-Client ihn sieht: JSON-RPC rein, Ereignisstrom raus.</summary>
public class McpEndpointTests
{
    private readonly McpTestApp app = McpTestApp.Create();

    [Fact]
    public async Task Initialize_AnswersWithServerInfoAndToolsCapability_WithoutSession()
    {
        var response = await app.RequestAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" },
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("text/event-stream", response.ContentType);
        Assert.False(response.Headers.ContainsKey("Mcp-Session-Id"));
        Assert.Equal("kundenportal", response.Result!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(response.Result!["capabilities"]!["tools"]);
    }

    [Fact]
    public async Task ToolsList_WithoutInitialize_ShowsOnlyAllowedTools()
    {
        var response = await app.RequestAsync("tools/list");

        Assert.Equal(["status", "wer_bin_ich"], response.ToolNames);
    }

    [Fact]
    public async Task ToolsList_WithCapabilities_ShowsRestrictedTools()
    {
        var response = await app.RequestAsync("tools/list", user: McpTestApp.User("invoices.write", "reports.read", "reports.export"));

        Assert.Equal(["status", "rechnung_anlegen", "wer_bin_ich", "bericht"], response.ToolNames);
    }

    [Fact]
    public async Task ToolsList_NeedsAllCapabilitiesOfClassAndMethod()
    {
        var response = await app.RequestAsync("tools/list", user: McpTestApp.User("reports.read"));

        Assert.DoesNotContain("bericht", response.ToolNames);
    }

    [Fact]
    public async Task ToolsList_DescribesArgumentsAsSchema()
    {
        var response = await app.RequestAsync("tools/list", user: McpTestApp.User("invoices.write"));

        var tool = response.Result!["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "rechnung_anlegen")!;
        Assert.Equal("Legt eine Rechnung an.", tool["description"]!.GetValue<string>());
        Assert.Equal(["kunde", "betrag"], tool["inputSchema"]!["required"]!.AsArray().Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public async Task ToolsCall_AllowedTool_ReturnsResult()
    {
        var response = await app.RequestAsync("tools/call", new { name = "rechnung_anlegen", arguments = new { kunde = "ACME", betrag = 12.5 } }, McpTestApp.User("invoices.write"));

        Assert.False(response.ToolIsError);
        Assert.Equal("ACME: 12.5 (Normal)", response.ToolText);
    }

    [Fact]
    public async Task ToolsCall_ToolSeesSignedInUserAndScopedServices()
    {
        var response = await app.RequestAsync("tools/call", new { name = "wer_bin_ich" });

        Assert.Equal("Hallo Erika Muster", response.ToolText);
    }

    [Fact]
    public async Task ToolsCall_WithoutCapability_IsRefusedWithMissingCapability()
    {
        var response = await app.RequestAsync("tools/call", new { name = "rechnung_anlegen", arguments = new { kunde = "ACME", betrag = 1 } });

        Assert.True(response.ToolIsError);
        Assert.Contains("„rechnung_anlegen“ ist für dich nicht freigegeben", response.ToolText);
        Assert.Contains("„invoices.write“", response.ToolText);
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_IsProtocolError()
    {
        var response = await app.RequestAsync("tools/call", new { name = "gibt_es_nicht" });

        Assert.Equal(-32602, response.Error!["code"]!.GetValue<int>());
        Assert.Contains("gibt_es_nicht", response.Error!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_UnknownArgument_IsRefusedInsteadOfIgnored()
    {
        var response = await app.RequestAsync("tools/call", new { name = "rechnung_anlegen", arguments = new { kunde = "ACME", betrag = 1, kundee = "x" } }, McpTestApp.User("invoices.write"));

        Assert.True(response.ToolIsError);
        Assert.Contains("Unbekanntes Argument „kundee“", response.ToolText);
    }

    [Fact]
    public async Task ToolsCall_WrongArgumentType_IsRefused()
    {
        var response = await app.RequestAsync("tools/call", new { name = "rechnung_anlegen", arguments = new { kunde = "ACME", betrag = "viel" } }, McpTestApp.User("invoices.write"));

        Assert.True(response.ToolIsError);
        Assert.Contains("„betrag“ muss vom Typ Zahl sein", response.ToolText);
    }

    [Fact]
    public async Task Notification_IsAcceptedWithoutBody()
    {
        var response = await app.PostAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Equal(202, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task InvalidBody_Is400()
    {
        var response = await app.PostAsync("kein json");

        Assert.Equal(400, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task GetAndDelete_Are405(string method)
    {
        var http = McpTestApp.CreateContext(method, "localhost:7071", McpTestApp.User());
        using var scope = app.Services.CreateScope();

        var result = await app.Endpoint.HandleAsync(http.Request, scope.ServiceProvider);
        await result.ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext { HttpContext = http });

        Assert.Equal(405, http.Response.StatusCode);
        Assert.Equal("POST", http.Response.Headers.Allow.ToString());
    }

    [Fact]
    public async Task Unauthenticated_Is401WithResourceMetadata()
    {
        var anonymous = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        var response = await app.PostAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", anonymous, host: "mcp.example.org");

        Assert.Equal(401, response.StatusCode);
        Assert.Equal(
            "Bearer resource_metadata=\"https://mcp.example.org/api/.well-known/oauth-protected-resource\"",
            response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task Instructions_AreSentInInitialize()
    {
        var withInstructions = McpTestApp.Create(configure: mcp => mcp.WithTools<SampleTools>().WithInstructions("Rufe zuerst status auf."));

        var response = await withInstructions.RequestAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" },
        });

        Assert.Equal("Rufe zuerst status auf.", response.Result!["instructions"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsCall_ArgumentsAreValidatedAlsoForNestedValues()
    {
        var response = await app.RequestAsync(
            "tools/call",
            new { name = "bericht", arguments = new JsonObject { ["jahr"] = 2026, ["monate"] = new JsonArray(1, "Februar") } },
            McpTestApp.User("reports.read", "reports.export"));

        Assert.True(response.ToolIsError);
        Assert.Contains("„monate[1]“ muss vom Typ ganze Zahl sein", response.ToolText);
    }

    [Fact]
    public async Task ToolsCall_McpException_ReachesTheModel()
    {
        var failing = McpTestApp.Create(configure: mcp => mcp.WithTools<FailingTools>());

        var response = await failing.RequestAsync("tools/call", new { name = "fachfehler" });

        Assert.True(response.ToolIsError);
        Assert.Contains("Kunde nicht gefunden.", response.ToolText);
    }

    [Fact]
    public async Task ToolsCall_UnexpectedException_KeepsDetailsInside()
    {
        var failing = McpTestApp.Create(configure: mcp => mcp.WithTools<FailingTools>());

        var response = await failing.RequestAsync("tools/call", new { name = "absturz" });

        Assert.True(response.ToolIsError);
        Assert.DoesNotContain("interne Einzelheit", response.ToolText);
    }
}
