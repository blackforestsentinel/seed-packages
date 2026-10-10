using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bfs.Seed.Mcp.Tests;

/// <summary>Baut eine App mit MCP wie im Template und spricht den Endpunkt per HTTP-Kontext an.</summary>
internal sealed class McpTestApp
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string ClientId = "22222222-2222-2222-2222-222222222222";

    private McpTestApp(ServiceProvider services) => Services = services;

    public ServiceProvider Services { get; }

    public SeedMcpEndpoint Endpoint => Services.GetRequiredService<SeedMcpEndpoint>();

    public static McpTestApp Create(Dictionary<string, string?>? settings = null, Action<SeedMcpBuilder>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:Project"] = "kundenportal",
                ["Seed:Features:Sso"] = "true",
                ["Auth:TenantId"] = TenantId,
                ["Auth:ClientId"] = ClientId,
                ["Auth:Audience"] = $"api://{ClientId}",
            })
            .AddInMemoryCollection(settings ?? [])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddOptions<SeedAuthOptions>().Bind(configuration.GetSection(SeedAuthOptions.SectionName));
        services.AddScoped<Greeter>();

        var builder = SeedMcpExtensions.Register(services, configuration);
        if (configure is null)
        {
            builder.WithTools<SampleTools>().WithTools<ReportTools>();
        }
        else
        {
            configure(builder);
        }

        return new McpTestApp(services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
    }

    public static ClaimsPrincipal User(params string[] capabilities)
    {
        var claims = new List<Claim> { new("name", "Erika Muster"), new("oid", "55555555-5555-5555-5555-555555555555") };
        claims.AddRange(capabilities.Select(c => new Claim(SeedClaimTypes.Capability, c)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer", nameType: "name", roleType: "roles"));
    }

    public async Task<McpResponse> PostAsync(string body, ClaimsPrincipal? user = null, string host = "func-kundenportal-dev.azurewebsites.net")
    {
        using var scope = Services.CreateScope();
        var http = CreateContext("POST", host, user ?? User());
        http.Request.ContentType = "application/json";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        var result = await Endpoint.HandleAsync(http.Request, scope.ServiceProvider);
        await result.ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext { HttpContext = http });
        return McpResponse.From(http);
    }

    public Task<McpResponse> RequestAsync(string method, object? parameters = null, ClaimsPrincipal? user = null, string host = "func-kundenportal-dev.azurewebsites.net")
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
        if (parameters is not null)
        {
            request["params"] = JsonSerializer.SerializeToNode(parameters);
        }

        return PostAsync(request.ToJsonString(), user, host);
    }

    public static DefaultHttpContext CreateContext(string method, string host, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext
        {
            User = user,
            RequestServices = new ServiceCollection()
                .AddLogging()
                .AddMvcCore().Services
                .BuildServiceProvider(),
        };
        http.Request.Method = method;
        http.Request.Scheme = "http";
        http.Request.Host = new HostString(host);
        http.Request.Path = SeedMcp.Path;
        http.Response.Body = new MemoryStream();
        return http;
    }
}

/// <summary>HTTP-Antwort des Endpunkts; bei SSE die JSON-RPC-Nachricht aus dem data-Feld.</summary>
internal sealed record McpResponse(int StatusCode, IHeaderDictionary Headers, string? ContentType, string Body)
{
    public static McpResponse From(HttpContext http)
    {
        http.Response.Body.Position = 0;
        var body = new StreamReader(http.Response.Body).ReadToEnd();
        return new McpResponse(http.Response.StatusCode, http.Response.Headers, http.Response.ContentType, body);
    }

    /// <summary>Die einzige JSON-RPC-Nachricht im Ereignisstrom.</summary>
    public JsonNode Message =>
        JsonNode.Parse(Body.Split('\n').Single(l => l.StartsWith("data:", StringComparison.Ordinal))["data:".Length..])!;

    public JsonNode? Result => Message["result"];

    public JsonNode? Error => Message["error"];

    public string[] ToolNames => Result!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();

    public string ToolText => Result!["content"]![0]!["text"]!.GetValue<string>();

    public bool ToolIsError => Result!["isError"]?.GetValue<bool>() == true;
}
