using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Nennt in 401- und 403-Antworten der Token-Prüfung für den MCP-Endpunkt dieselben Scopes wie die
/// Metadaten (<c>scopes_supported</c>): den Scope der Ressource und <c>offline_access</c>. Clients wie
/// Claude nehmen den Scope aus dieser ersten Antwort und fordern ihn bei Entra an.
/// </summary>
internal sealed class SeedMcpChallengeScope(IOptions<SeedMcpOptions> options, IOptions<SeedAuthOptions> authOptions)
    : ISeedAuthChallengeScope
{
    public string? For(HttpRequest request) =>
        IsMcpPath(request.Path)
            ? $"{SeedMcpResource.For(request, options.Value, authOptions.Value).Scope} {SeedMcpEndpoint.OfflineAccess}"
            : null;

    private static bool IsMcpPath(PathString path) =>
        string.Equals(path.Value?.TrimEnd('/'), SeedMcp.Path, StringComparison.OrdinalIgnoreCase);
}
