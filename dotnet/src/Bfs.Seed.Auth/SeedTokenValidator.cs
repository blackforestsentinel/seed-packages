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
    /// <summary>Token gültig, aber ohne akzeptierte delegierte Berechtigung.</summary>
    public const string MissingScope = "missing_scope";

    /// <summary>Token ohne angemeldete Person und ohne App-Rolle für Anwendungen.</summary>
    public const string MissingAppRole = "missing_app_role";

    /// <summary>Ob das Token gültig ist.</summary>
    public bool IsValid => Principal is not null;

    /// <summary>Ob das Token gültig ist, ihm aber eine Berechtigung fehlt (403 statt 401).</summary>
    public bool IsInsufficient => Error is MissingScope or MissingAppRole;

    internal static SeedTokenValidationResult Fail(string error) => new(null, error);
}

/// <summary>
/// Prüft Bearer-Tokens aus Entra ID: Signatur gegen die Schlüssel des Tenants, Aussteller,
/// Audience, Laufzeit und die delegierte Berechtigung. Tokens ohne angemeldete Person (ohne
/// <c>scp</c>) gelten nur mit einer App-Rolle, die <c>auth.roles</c> für Anwendungen vorsieht.
/// Die Capabilities aus den App-Rollen hängt die Prüfung als <see cref="SeedClaimTypes.Capability"/> an.
/// </summary>
public sealed class SeedTokenValidator
{
    private readonly JsonWebTokenHandler handler = new() { MapInboundClaims = false };
    private readonly IOptions<SeedAuthOptions> options;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> metadata;
    private readonly SeedCapabilityMap capabilities;

    /// <summary>Prüfung ohne Zuordnung von Rollen zu Capabilities.</summary>
    public SeedTokenValidator(IOptions<SeedAuthOptions> options, IConfigurationManager<OpenIdConnectConfiguration> metadata)
        : this(options, metadata, SeedCapabilityMap.Empty)
    {
    }

    /// <summary>Prüfung mit der Zuordnung aus <c>auth.roles</c>.</summary>
    public SeedTokenValidator(
        IOptions<SeedAuthOptions> options,
        IConfigurationManager<OpenIdConnectConfiguration> metadata,
        SeedCapabilityMap capabilities)
    {
        this.options = options;
        this.metadata = metadata;
        this.capabilities = capabilities;
    }

    /// <summary>Prüft ein Token gegen <see cref="SeedAuthOptions.RequiredScope"/>.</summary>
    public Task<SeedTokenValidationResult> ValidateAsync(string token, CancellationToken cancellationToken = default) =>
        ValidateAsync(token, [options.Value.RequiredScope], cancellationToken);

    /// <summary>
    /// Prüft ein Token und liefert bei Erfolg die angemeldete Person oder Anwendung. Bei einem
    /// delegierten Token muss eine der <paramref name="acceptedScopes"/> im <c>scp</c>-Claim stehen.
    /// </summary>
    public async Task<SeedTokenValidationResult> ValidateAsync(
        string token,
        IReadOnlyCollection<string> acceptedScopes,
        CancellationToken cancellationToken = default)
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

        var identity = result.ClaimsIdentity;
        var roles = identity.FindAll(SeedClaimTypes.Role).Select(c => c.Value).ToList();
        SeedMemberTypes memberType;

        if (IsAppOnly(identity))
        {
            // Ohne angemeldete Person gibt es keine delegierte Berechtigung. Zugelassen sind nur
            // Anwendungen mit einer Rolle, die auth.roles ausdrücklich für Anwendungen vorsieht.
            if (!capabilities.AllowsApplication(roles))
            {
                return SeedTokenValidationResult.Fail(SeedTokenValidationResult.MissingAppRole);
            }

            memberType = SeedMemberTypes.Application;
        }
        else
        {
            var scopes = identity.FindFirst("scp")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (!scopes.Intersect(acceptedScopes, StringComparer.Ordinal).Any())
            {
                return SeedTokenValidationResult.Fail(SeedTokenValidationResult.MissingScope);
            }

            memberType = SeedMemberTypes.User;
        }

        foreach (var capability in capabilities.Resolve(roles, memberType))
        {
            identity.AddClaim(new Claim(SeedClaimTypes.Capability, capability));
        }

        return new SeedTokenValidationResult(new ClaimsPrincipal(identity), null);
    }

    // Delegierte Tokens tragen immer scp. idtyp=app setzt Entra nur als optionalen Claim,
    // reicht aber allein, um ein Token als App-only zu erkennen.
    internal static bool IsAppOnly(ClaimsIdentity identity) =>
        string.Equals(identity.FindFirst("idtyp")?.Value, "app", StringComparison.OrdinalIgnoreCase)
        || identity.FindFirst("scp") is null;

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
            RoleClaimType = SeedClaimTypes.Role,
        });
    }
}
