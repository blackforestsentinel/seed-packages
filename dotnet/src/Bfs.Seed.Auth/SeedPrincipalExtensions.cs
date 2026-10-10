using System.Security.Claims;

namespace Bfs.Seed.Auth;

/// <summary>Zugriff auf Rollen und Capabilities der angemeldeten Person oder Anwendung.</summary>
public static class SeedPrincipalExtensions
{
    /// <summary>Prüft, ob die Capability aus den App-Rollen folgt.</summary>
    public static bool HasCapability(this ClaimsPrincipal principal, string capability) =>
        principal.HasClaim(c => c.Type == SeedClaimTypes.Capability && string.Equals(c.Value, capability, StringComparison.Ordinal));

    /// <summary>Alle Capabilities, die aus den App-Rollen folgen.</summary>
    public static IReadOnlySet<string> GetCapabilities(this ClaimsPrincipal principal) =>
        principal.FindAll(SeedClaimTypes.Capability).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>App-Rollen aus dem Token.</summary>
    public static IReadOnlySet<string> GetRoles(this ClaimsPrincipal principal) =>
        principal.FindAll(SeedClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
}
