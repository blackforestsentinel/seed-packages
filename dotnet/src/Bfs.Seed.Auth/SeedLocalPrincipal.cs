using System.Security.Claims;

namespace Bfs.Seed.Auth;

/// <summary>Angemeldete Person für <see cref="SeedAuthMode.Local"/>, aufgebaut wie ein delegiertes Token.</summary>
internal static class SeedLocalPrincipal
{
    internal const string AuthenticationType = "SeedLocal";

    internal static ClaimsPrincipal Create(SeedAuthOptions options, SeedCapabilityMap map)
    {
        var user = options.LocalUser;
        var identity = new ClaimsIdentity(
            [
                new Claim("name", user.Name),
                new Claim("preferred_username", user.Username),
                new Claim("oid", user.ObjectId),
                new Claim("tid", user.TenantId),
                new Claim("scp", options.RequiredScope),
            ],
            AuthenticationType,
            "name",
            SeedClaimTypes.Role);

        identity.AddClaims(user.Roles.Select(r => new Claim(SeedClaimTypes.Role, r)));
        identity.AddClaims(map.Resolve(user.Roles, SeedMemberTypes.User).Select(c => new Claim(SeedClaimTypes.Capability, c)));
        return new ClaimsPrincipal(identity);
    }
}
