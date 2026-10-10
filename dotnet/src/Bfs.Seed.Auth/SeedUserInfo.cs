using System.Security.Claims;

namespace Bfs.Seed.Auth;

/// <summary>
/// Antwort für <c>GET /api/me</c>: wer angemeldet ist und welche Capabilities daraus folgen.
/// Das Frontend blendet damit UI-Elemente ein oder aus, ohne die Zuordnung zu kennen;
/// <c>@blackforestsentinel/seed-web-auth</c> liest genau diese Form.
/// </summary>
/// <param name="Name">Anzeigename, bei Anwendungen <c>null</c>.</param>
/// <param name="Username">Benutzername (<c>preferred_username</c>, bei v1-Tokens <c>upn</c>).</param>
/// <param name="ObjectId">Object-ID der Person oder Anwendung (<c>oid</c>).</param>
/// <param name="TenantId">Tenant-ID (<c>tid</c>).</param>
/// <param name="Roles">App-Rollen aus dem Token, sortiert.</param>
/// <param name="Capabilities">Capabilities aus den App-Rollen, sortiert.</param>
public sealed record SeedUserInfo(
    string? Name,
    string? Username,
    string? ObjectId,
    string? TenantId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities)
{
    /// <summary>Liest die Werte aus <c>HttpContext.User</c>; ohne Anmeldung bleiben sie leer.</summary>
    public static SeedUserInfo From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return new SeedUserInfo(
            Name: principal.FindFirst("name")?.Value,
            Username: principal.FindFirst("preferred_username")?.Value ?? principal.FindFirst("upn")?.Value ?? principal.FindFirst("unique_name")?.Value,
            ObjectId: principal.FindFirst("oid")?.Value,
            TenantId: principal.FindFirst("tid")?.Value,
            Roles: [.. principal.GetRoles().Order(StringComparer.Ordinal)],
            Capabilities: [.. principal.GetCapabilities().Order(StringComparer.Ordinal)]);
    }
}
