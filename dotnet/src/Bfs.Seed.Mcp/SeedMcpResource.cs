using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Http;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Kanonische Adresse des Endpunkts und der Scope, den ein Client dafür bei Entra anfordert.
/// </summary>
/// <param name="Url">Adresse des MCP-Endpunkts, z. B. <c>https://mcp.example.org/api/mcp</c>.</param>
/// <param name="Scope">Voll qualifizierter Scope, z. B. <c>api://&lt;client-id&gt;/mcp_access</c>.</param>
internal sealed record SeedMcpResource(string Url, string Scope)
{
    /// <summary>
    /// Leitet Adresse und Scope aus der Anfrage ab. Kommt sie über die eigene Domain aus
    /// <c>Mcp:Resource</c>, gilt diese Adresse samt Scope darunter: Claude sendet sie als
    /// <c>resource</c>, und Entra verlangt dann einen Scope derselben Application ID URI. Über den
    /// Standardnamen der Function gilt <c>api://&lt;client-id&gt;</c>, das ohne <c>resource</c>
    /// anmeldende Clients wie VS Code verwenden.
    /// </summary>
    public static SeedMcpResource For(HttpRequest request, SeedMcpOptions mcp, SeedAuthOptions auth)
    {
        if (TryParse(mcp.Resource, out var configured)
            && string.Equals(configured.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            var url = configured.GetLeftPart(UriPartial.Path);
            return new SeedMcpResource(url, $"{url}/{SeedMcp.Scope}");
        }

        var audience = string.IsNullOrWhiteSpace(auth.Audience) ? $"api://{auth.ClientId}" : auth.Audience.TrimEnd('/');
        return new SeedMcpResource($"{BaseUrl(request)}{SeedMcp.Path}", $"{audience}/{SeedMcp.Scope}");
    }

    /// <summary>
    /// Schema und Host, unter denen der Client die Function erreicht. Hinter dem Functions-Host
    /// kommt die Anfrage intern per HTTP an; nach außen gilt HTTPS, lokal das tatsächliche Schema.
    /// Dieselbe Regel wie für <c>resource_metadata</c> in Bfs.Seed.Auth.
    /// </summary>
    public static string BaseUrl(HttpRequest request)
    {
        var scheme = request.Host.Host is "localhost" or "127.0.0.1" ? request.Scheme : "https";
        return $"{scheme}://{request.Host}";
    }

    /// <summary>Leer oder eine https-Adresse (lokal auch http), die genau auf <c>/api/mcp</c> endet.</summary>
    public static bool IsValidConfiguredResource(string? value) =>
        string.IsNullOrWhiteSpace(value) || TryParse(value, out _);

    private static bool TryParse(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || !(parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback))
            || parsed.AbsolutePath != SeedMcp.Path
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
