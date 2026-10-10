using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bfs.Seed.Auth;

/// <summary>Ergebnis der Token-Prüfung.</summary>
/// <param name="Principal">Angemeldete Person, wenn das Token gültig ist.</param>
/// <param name="Error">Grund der Ablehnung, sonst <c>null</c>.</param>
public sealed record SeedTokenValidationResult(ClaimsPrincipal? Principal, string? Error)
{
    /// <summary>Ob das Token gültig ist.</summary>
    public bool IsValid => Principal is not null;

    internal static SeedTokenValidationResult Fail(string error) => new(null, error);
}

/// <summary>
/// Prüft Bearer-Tokens aus Entra ID: Signatur gegen die Schlüssel des Tenants, Aussteller,
/// Audience, Laufzeit und die delegierte Berechtigung.
/// </summary>
public sealed class SeedTokenValidator(
    IOptions<SeedAuthOptions> options,
    IConfigurationManager<OpenIdConnectConfiguration> metadata)
{
    private readonly JsonWebTokenHandler handler = new() { MapInboundClaims = false };

    /// <summary>Prüft ein Token und liefert bei Erfolg die angemeldete Person.</summary>
    public async Task<SeedTokenValidationResult> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        var result = await ValidateSignedAsync(token, cancellationToken);

        // Entra ID rotiert Signaturschlüssel; bei unbekanntem Schlüssel einmal neu laden.
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            metadata.RequestRefresh();
            result = await ValidateSignedAsync(token, cancellationToken);
        }

        if (!result.IsValid)
        {
            return SeedTokenValidationResult.Fail(result.Exception?.GetType().Name ?? "invalid_token");
        }

        var scopes = result.ClaimsIdentity.FindFirst("scp")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (!scopes.Contains(options.Value.RequiredScope, StringComparer.Ordinal))
        {
            return SeedTokenValidationResult.Fail("missing_scope");
        }

        return new SeedTokenValidationResult(new ClaimsPrincipal(result.ClaimsIdentity), null);
    }

    private async Task<TokenValidationResult> ValidateSignedAsync(string token, CancellationToken cancellationToken)
    {
        var configuration = await metadata.GetConfigurationAsync(cancellationToken);
        var o = options.Value;

        return await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuers = o.ValidIssuers,
            ValidAudiences = o.ValidAudiences,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = "name",
            RoleClaimType = "roles",
        });
    }
}
