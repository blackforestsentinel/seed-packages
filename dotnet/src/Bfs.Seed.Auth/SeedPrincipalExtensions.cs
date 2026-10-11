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

    /// <summary>
    /// App-Rollen aus dem Token mit ihren Anzeigenamen aus <c>auth.roles.&lt;Rolle&gt;.displayName</c>,
    /// sortiert nach dem Wert. Die Token-Prüfung hängt die Anzeigenamen als
    /// <see cref="SeedClaimTypes.RoleDisplayName"/> an; fehlt einer, etwa bei einer Rolle, die
    /// project.yaml nicht kennt, gilt der Wert der Rolle.
    /// </summary>
    public static IReadOnlyList<SeedRoleDetail> GetRoleDetails(this ClaimsPrincipal principal)
    {
        var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var claim in principal.FindAll(SeedClaimTypes.RoleDisplayName))
        {
            if (claim.Properties.TryGetValue(SeedClaimTypes.RoleProperty, out var role))
            {
                displayNames.TryAdd(role, claim.Value);
            }
        }

        return [.. principal.GetRoles()
            .Order(StringComparer.Ordinal)
            .Select(r => new SeedRoleDetail(r, displayNames.GetValueOrDefault(r, r)))];
    }

    /// <summary>
    /// Ob eine Anwendung ohne angemeldete Person aufruft (Token ohne <c>scp</c>). Ihre
    /// Capabilities stammen aus App-Rollen für den Mitgliedstyp Application.
    /// </summary>
    public static bool IsApplication(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true && principal.FindFirst("scp") is null;
}
