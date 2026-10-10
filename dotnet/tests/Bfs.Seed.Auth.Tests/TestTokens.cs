using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bfs.Seed.Auth.Tests;

/// <summary>Tokens wie aus Entra ID, signiert mit einem eigenen Testschlüssel.</summary>
public sealed class TestTokens
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string ClientId = "22222222-2222-2222-2222-222222222222";

    private readonly RsaSecurityKey signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    public SeedAuthOptions AuthOptions { get; } = new() { TenantId = TenantId, ClientId = ClientId };

    public SeedTokenValidator CreateValidator(SeedCapabilityMap? map = null)
    {
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(signingKey);
        var metadata = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        return map is null
            ? new SeedTokenValidator(Options.Create(AuthOptions), metadata)
            : new SeedTokenValidator(Options.Create(AuthOptions), metadata, map);
    }

    public string CreateToken(
        string? audience = null,
        string? issuer = null,
        string? scope = "access_as_user",
        DateTime? expires = null,
        SecurityKey? key = null,
        string[]? roles = null,
        string? idtyp = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(30);
        var claims = new Dictionary<string, object> { ["oid"] = "55555555-5555-5555-5555-555555555555", ["tid"] = TenantId };
        if (scope is not null)
        {
            // Delegiertes Token mit angemeldeter Person, sonst App-only wie im Client-Credentials-Flow.
            claims["scp"] = scope;
            claims["name"] = "Erika Muster";
            claims["preferred_username"] = "erika@example.org";
        }

        if (roles is not null)
        {
            claims["roles"] = roles;
        }

        if (idtyp is not null)
        {
            claims["idtyp"] = idtyp;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? $"https://login.microsoftonline.com/{TenantId}/v2.0",
            Audience = audience ?? ClientId,
            NotBefore = expiry.AddMinutes(-60),
            IssuedAt = expiry.AddMinutes(-60),
            Expires = expiry,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? signingKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
