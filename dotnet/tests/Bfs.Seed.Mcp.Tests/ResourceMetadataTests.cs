using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace Bfs.Seed.Mcp.Tests;

/// <summary>Metadaten nach RFC 9728: Feldnamen und Werte, wie Clients sie lesen.</summary>
public class ResourceMetadataTests
{
    [Fact]
    public void DefaultHost_NamesTenantAndApiScope()
    {
        var json = Metadata(McpTestApp.Create(), "func-kundenportal-dev.azurewebsites.net");

        Assert.Equal(
            ["resource", "authorization_servers", "scopes_supported", "bearer_methods_supported", "resource_name"],
            json.AsObject().Select(p => p.Key));
        Assert.Equal("https://func-kundenportal-dev.azurewebsites.net/api/mcp", json["resource"]!.GetValue<string>());
        Assert.Equal([$"https://login.microsoftonline.com/{McpTestApp.TenantId}/v2.0"], Strings(json["authorization_servers"]));
        Assert.Equal([$"api://{McpTestApp.ClientId}/mcp_access", "offline_access"], Strings(json["scopes_supported"]));
        Assert.Equal(["header"], Strings(json["bearer_methods_supported"]));
        Assert.Equal("kundenportal", json["resource_name"]!.GetValue<string>());
    }

    [Fact]
    public void CustomDomain_UsesConfiguredResourceAndScopeBelowIt()
    {
        var app = McpTestApp.Create(new() { ["Mcp:Resource"] = "https://mcp.example.org/api/mcp" });

        var json = Metadata(app, "MCP.example.org");

        Assert.Equal("https://mcp.example.org/api/mcp", json["resource"]!.GetValue<string>());
        Assert.Equal(["https://mcp.example.org/api/mcp/mcp_access", "offline_access"], Strings(json["scopes_supported"]));
    }

    [Fact]
    public void CustomDomainConfigured_DefaultHostKeepsApiScope()
    {
        var app = McpTestApp.Create(new() { ["Mcp:Resource"] = "https://mcp.example.org/api/mcp" });

        var json = Metadata(app, "func-kundenportal-dev.azurewebsites.net");

        Assert.Equal("https://func-kundenportal-dev.azurewebsites.net/api/mcp", json["resource"]!.GetValue<string>());
        Assert.Equal($"api://{McpTestApp.ClientId}/mcp_access", Strings(json["scopes_supported"])[0]);
    }

    [Fact]
    public void Localhost_KeepsHttpAndPort()
    {
        var json = Metadata(McpTestApp.Create(), "localhost:7071");

        Assert.Equal("http://localhost:7071/api/mcp", json["resource"]!.GetValue<string>());
    }

    [Fact]
    public void WithoutTenant_Is404()
    {
        var app = McpTestApp.Create(new() { ["Auth:TenantId"] = "" });
        var http = McpTestApp.CreateContext("GET", "localhost:7071", McpTestApp.User());

        Assert.IsType<NotFoundResult>(app.Endpoint.ResourceMetadata(http.Request));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://mcp.example.org/api/mcp", true)]
    [InlineData("http://localhost:7071/api/mcp", true)]
    [InlineData("http://mcp.example.org/api/mcp", false)]
    [InlineData("https://mcp.example.org/api/mcp/", false)]
    [InlineData("https://mcp.example.org/mcp", false)]
    [InlineData("https://mcp.example.org/api/mcp?x=1", false)]
    [InlineData("mcp.example.org", false)]
    public void ConfiguredResource_IsValidated(string? value, bool valid)
    {
        Assert.Equal(valid, SeedMcpResource.IsValidConfiguredResource(value));
    }

    private static JsonNode Metadata(McpTestApp app, string host)
    {
        var http = McpTestApp.CreateContext("GET", host, McpTestApp.User());
        var result = Assert.IsType<ContentResult>(app.Endpoint.ResourceMetadata(http.Request));
        Assert.Equal("application/json", result.ContentType);
        return JsonNode.Parse(result.Content!)!;
    }

    private static string[] Strings(JsonNode? array) => array!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
}
