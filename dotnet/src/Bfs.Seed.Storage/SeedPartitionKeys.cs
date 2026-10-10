using System.Security.Claims;

namespace Bfs.Seed.Storage;

/// <summary>
/// Konvention für die Trennung von Daten: PartitionKey = Object-ID der Person (<c>oid</c>) für
/// persönliche Daten, = Tenant-ID (<c>tid</c>) für Daten einer Organisation. Ein Datensatz liegt
/// so immer in der Partition seiner Eigentümerin, und jede Abfrage bleibt auf sie begrenzt.
/// </summary>
/// <remarks>
/// Liest die Claims aus dem <see cref="ClaimsPrincipal"/> der Anfrage, ohne Abhängigkeit von
/// Bfs.Seed.Auth: kurze Namen wie im Token (<c>oid</c>, <c>tid</c>, so setzt sie Bfs.Seed.Auth)
/// oder die langen Namen nach dem Claim-Mapping von ASP.NET Core.
/// </remarks>
public static class SeedPartitionKeys
{
    /// <summary>Langer Name von <c>oid</c> nach dem Claim-Mapping von ASP.NET Core.</summary>
    public const string ObjectIdClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>Langer Name von <c>tid</c> nach dem Claim-Mapping von ASP.NET Core.</summary>
    public const string TenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    /// <summary>
    /// PartitionKey der angemeldeten Person: ihre Object-ID (<c>oid</c>), stabil über Namens- und
    /// Mailänderungen hinweg. Ohne <c>oid</c> (nicht angemeldet) eine Ausnahme, damit keine Daten
    /// ohne Eigentümerin entstehen.
    /// </summary>
    public static string ForUser(ClaimsPrincipal principal) => Require(principal, "oid", ObjectIdClaimType);

    /// <summary>PartitionKey der Organisation der angemeldeten Person: ihre Tenant-ID (<c>tid</c>).</summary>
    public static string ForTenant(ClaimsPrincipal principal) => Require(principal, "tid", TenantIdClaimType);

    /// <summary>Wie <see cref="ForUser"/>, aber <c>null</c> statt einer Ausnahme.</summary>
    public static string? TryForUser(ClaimsPrincipal? principal) => Find(principal, "oid", ObjectIdClaimType);

    /// <summary>Wie <see cref="ForTenant"/>, aber <c>null</c> statt einer Ausnahme.</summary>
    public static string? TryForTenant(ClaimsPrincipal? principal) => Find(principal, "tid", TenantIdClaimType);

    private static string Require(ClaimsPrincipal principal, string shortType, string longType)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Find(principal, shortType, longType)
            ?? throw new InvalidOperationException(
                $"Kein gültiger Claim '{shortType}' in der Anmeldung. Ist die Function per Token geschützt (Feature sso, UseSeedAuth)?");
    }

    private static string? Find(ClaimsPrincipal? principal, string shortType, string longType)
    {
        var value = (principal?.FindFirst(shortType) ?? principal?.FindFirst(longType))?.Value;

        // Entra ID liefert GUIDs; einheitlich klein geschrieben, damit dieselbe Person immer
        // dieselbe Partition trifft. Alles andere ist kein gültiger Schlüssel.
        return Guid.TryParse(value, out var id) ? id.ToString("D") : null;
    }
}
