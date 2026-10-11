using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bfs.Seed.Mcp.Tests;

/// <summary>Scope in der 401 der Token-Prüfung: derselbe wie in scopes_supported der Metadaten.</summary>
public class ChallengeScopeTests
{
    [Fact]
    public void McpPath_NamesResourceScopeAndOfflineAccess()
    {
        var app = McpTestApp.Create(new() { ["Mcp:Resource"] = "https://mcp.example.org/api/mcp" });
        var challenge = Assert.Single(app.Services.GetServices<ISeedAuthChallengeScope>());

        Assert.Equal("https://mcp.example.org/api/mcp/mcp_access offline_access", challenge.For(Request("mcp.example.org", "/api/mcp")));
        Assert.Equal(
            $"api://{McpTestApp.ClientId}/mcp_access offline_access",
            challenge.For(Request("func-kundenportal-dev.azurewebsites.net", "/api/mcp")));
    }

    [Fact]
    public void OtherPaths_HaveNoChallengeScope()
    {
        var challenge = McpTestApp.Create().Services.GetServices<ISeedAuthChallengeScope>().Single();

        Assert.Null(challenge.For(Request("func-kundenportal-dev.azurewebsites.net", "/api/me")));
        Assert.Null(challenge.For(Request("func-kundenportal-dev.azurewebsites.net", "/api/.well-known/oauth-protected-resource")));
    }

    private static HttpRequest Request(string host, string path)
    {
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString(host);
        http.Request.Path = path;
        return http.Request;
    }
}
