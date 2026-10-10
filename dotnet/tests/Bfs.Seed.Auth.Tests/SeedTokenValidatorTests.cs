using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Bfs.Seed.Auth.Tests;

public class SeedTokenValidatorTests
{
    private const string TenantId = TestTokens.TenantId;
    private const string ClientId = TestTokens.ClientId;

    [Fact]
    public async Task ValidToken_ReturnsPrincipalWithName()
    {
        var result = await CreateValidator().ValidateAsync(CreateToken(), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal("Erika Muster", result.Principal!.Identity!.Name);
    }

    [Fact]
    public async Task AppIdUriAsAudience_IsAccepted()
    {
        var result = await CreateValidator().ValidateAsync(CreateToken(audience: $"api://{ClientId}"), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task V1Issuer_IsAccepted()
    {
        var result = await CreateValidator().ValidateAsync(CreateToken(issuer: $"https://sts.windows.net/{TenantId}/"), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("33333333-3333-3333-3333-333333333333", null)]
    [InlineData(null, "https://login.microsoftonline.com/44444444-4444-4444-4444-444444444444/v2.0")]
    public async Task WrongAudienceOrIssuer_IsRejected(string? audience, string? issuer)
    {
        var result = await CreateValidator().ValidateAsync(CreateToken(audience: audience, issuer: issuer), TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var result = await CreateValidator().ValidateAsync(CreateToken(expires: DateTime.UtcNow.AddMinutes(-10)), TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ForeignSigningKey_IsRejected()
    {
        var foreignKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };

        var result = await CreateValidator().ValidateAsync(CreateToken(key: foreignKey), TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task MissingScope_IsRejected()
    {
        var result = await CreateValidator().ValidateAsync(CreateToken(scope: "other_scope"), TestContext.Current.CancellationToken);

        Assert.Equal("missing_scope", result.Error);
    }

    [Fact]
    public async Task AcceptedScopes_ReplaceRequiredScope()
    {
        var validator = CreateValidator();

        var mcp = await validator.ValidateAsync(CreateToken(scope: "mcp_access"), ["mcp_access", "other"], TestContext.Current.CancellationToken);
        var user = await validator.ValidateAsync(CreateToken(), ["mcp_access"], TestContext.Current.CancellationToken);

        Assert.True(mcp.IsValid);
        Assert.Equal(SeedTokenValidationResult.MissingScope, user.Error);
        Assert.True(user.IsInsufficient);
    }

    [Fact]
    public async Task Roles_ResolveToCapabilityClaims()
    {
        var result = await CreateValidator(Map).ValidateAsync(CreateToken(roles: ["Reader", "Admin"]), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(["invoices.read", "invoices.write"], result.Principal!.GetCapabilities().Order());
        Assert.Equal(["Admin", "Reader"], result.Principal!.GetRoles().Order());
        Assert.True(result.Principal!.IsInRole("Admin"));
        Assert.False(result.Principal!.IsApplication());
    }

    [Fact]
    public async Task DelegatedToken_GetsNoCapabilitiesFromApplicationOnlyRole()
    {
        var result = await CreateValidator(Map).ValidateAsync(CreateToken(roles: ["Sync"]), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Empty(result.Principal!.GetCapabilities());
    }

    [Fact]
    public async Task AppOnlyToken_WithApplicationRole_IsAccepted()
    {
        var result = await CreateValidator(Map).ValidateAsync(CreateToken(scope: null, roles: ["Sync", "Reader"]), TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.True(result.Principal!.IsApplication());
        Assert.Equal(["invoices.sync"], result.Principal!.GetCapabilities());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Reader")]
    [InlineData("Unbekannt")]
    public async Task AppOnlyToken_WithoutApplicationRole_IsRejected(string roles)
    {
        var token = CreateToken(scope: null, roles: roles.Split(',', StringSplitOptions.RemoveEmptyEntries));

        var result = await CreateValidator(Map).ValidateAsync(token, TestContext.Current.CancellationToken);

        Assert.Equal(SeedTokenValidationResult.MissingAppRole, result.Error);
    }

    [Fact]
    public async Task AppOnlyToken_WithoutMap_IsRejected()
    {
        var result = await CreateValidator().ValidateAsync(CreateToken(scope: null, roles: ["Sync"]), TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task IdtypApp_IsTreatedAsAppOnly()
    {
        var result = await CreateValidator(Map).ValidateAsync(CreateToken(scope: null, roles: ["Admin"], idtyp: "app"), TestContext.Current.CancellationToken);

        Assert.True(result.Principal!.IsApplication());
        Assert.Equal(["invoices.read", "invoices.write"], result.Principal!.GetCapabilities().Order());
    }

    [Fact]
    public void AllowAnonymous_IsDetectedOnMethodAndClass()
    {
        Assert.True(SeedAuthMiddleware.AllowsAnonymous($"{typeof(SampleFunctions).FullName}.{nameof(SampleFunctions.Health)}"));
        Assert.False(SeedAuthMiddleware.AllowsAnonymous($"{typeof(SampleFunctions).FullName}.{nameof(SampleFunctions.Me)}"));
        Assert.True(SeedAuthMiddleware.AllowsAnonymous($"{typeof(PublicFunctions).FullName}.{nameof(PublicFunctions.Info)}"));
    }

    private static readonly SeedCapabilityMap Map = SeedCapabilityMap.Parse(SeedCapabilityMapTests.ProjectYaml);

    private readonly TestTokens tokens = new();

    private SeedTokenValidator CreateValidator(SeedCapabilityMap? map = null) => tokens.CreateValidator(map);

    private string CreateToken(
        string? audience = null,
        string? issuer = null,
        string? scope = "access_as_user",
        DateTime? expires = null,
        SecurityKey? key = null,
        string[]? roles = null,
        string? idtyp = null) =>
        tokens.CreateToken(audience, issuer, scope, expires, key, roles, idtyp);

    public sealed class SampleFunctions
    {
        [AllowAnonymous]
        public string Health() => "ok";

        public string Me() => "me";
    }

    [AllowAnonymous]
    public sealed class PublicFunctions
    {
        public string Info() => "info";
    }
}
