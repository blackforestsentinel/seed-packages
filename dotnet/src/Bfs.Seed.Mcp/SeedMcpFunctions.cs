using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Die beiden HTTP-Functions des MCP-Servers. Sie kommen mit dem Paket in jede App, die es
/// referenziert; ohne <c>AddSeedMcp()</c> antworten beide mit 404.
/// </summary>
public sealed class SeedMcpFunctions(IServiceProvider services)
{
    /// <summary>
    /// <c>POST /api/mcp</c>: eine JSON-RPC-Nachricht, eine Antwort (Streamable HTTP, zustandslos).
    /// Die Token-Prüfung aus Bfs.Seed.Auth verlangt dafür den Scope <c>mcp_access</c>.
    /// </summary>
    [Function("SeedMcp")]
    [RequireScope(SeedMcp.Scope)]
    public Task<IActionResult> McpAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "delete", Route = SeedMcp.Route)] HttpRequest request,
        FunctionContext context)
    {
        var endpoint = services.GetService<SeedMcpEndpoint>();
        return endpoint is null
            ? Task.FromResult<IActionResult>(new NotFoundResult())
            : endpoint.HandleAsync(request, context.InstanceServices);
    }

    /// <summary>
    /// <c>GET /api/.well-known/oauth-protected-resource</c>: Metadaten nach RFC 9728. Ohne Anmeldung,
    /// weil ein Client hier erst erfährt, wo er sich anmelden muss.
    /// </summary>
    [Function("SeedMcpResourceMetadata")]
    [AllowAnonymous]
    public IActionResult ResourceMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = SeedMcp.ResourceMetadataRoute)] HttpRequest request)
    {
        var endpoint = services.GetService<SeedMcpEndpoint>();
        return endpoint is null ? new NotFoundResult() : endpoint.ResourceMetadata(request);
    }
}
