using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bfs.Seed.Auth.Tests;

public class SeedTokenValidatorTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string ClientId = "22222222-2222-2222-2222-222222222222";

    private readonly RsaSecurityKey signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly SeedAuthOptions options = new() { TenantId = TenantId, ClientId = ClientId };

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
    public void AllowAnonymous_IsDetectedOnMethodAndClass()
    {
        Assert.True(SeedAuthMiddleware.AllowsAnonymous($"{typeof(SampleFunctions).FullName}.{nameof(SampleFunctions.Health)}"));
        Assert.False(SeedAuthMiddleware.AllowsAnonymous($"{typeof(SampleFunctions).FullName}.{nameof(SampleFunctions.Me)}"));
        Assert.True(SeedAuthMiddleware.AllowsAnonymous($"{typeof(PublicFunctions).FullName}.{nameof(PublicFunctions.Info)}"));
    }

    private SeedTokenValidator CreateValidator()
    {
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(signingKey);
        return new SeedTokenValidator(Options.Create(options), new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
    }

    private string CreateToken(
        string? audience = null,
        string? issuer = null,
        string scope = "access_as_user",
        DateTime? expires = null,
        SecurityKey? key = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? $"https://login.microsoftonline.com/{TenantId}/v2.0",
            Audience = audience ?? ClientId,
            NotBefore = expiry.AddMinutes(-60),
            IssuedAt = expiry.AddMinutes(-60),
            Expires = expiry,
            Claims = new Dictionary<string, object> { ["scp"] = scope, ["name"] = "Erika Muster", ["oid"] = "55555555-5555-5555-5555-555555555555" },
            SigningCredentials = new SigningCredentials(key ?? signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

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
