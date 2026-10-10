using System.Security.Claims;

namespace Bfs.Seed.Storage.Tests;

public class SeedPartitionKeysTests
{
    private const string ObjectId = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string TenantId = "bbbbbbbb-1111-2222-3333-444444444444";

    [Fact]
    public void ShortClaims_AsSetByBfsSeedAuth()
    {
        var principal = Principal(("oid", ObjectId), ("tid", TenantId));

        Assert.Equal(ObjectId, SeedPartitionKeys.ForUser(principal));
        Assert.Equal(TenantId, SeedPartitionKeys.ForTenant(principal));
    }

    [Fact]
    public void LongClaims_AfterAspNetCoreMapping()
    {
        var principal = Principal(
            (SeedPartitionKeys.ObjectIdClaimType, ObjectId),
            (SeedPartitionKeys.TenantIdClaimType, TenantId));

        Assert.Equal(ObjectId, SeedPartitionKeys.ForUser(principal));
        Assert.Equal(TenantId, SeedPartitionKeys.ForTenant(principal));
    }

    [Fact]
    public void Guid_IsNormalizedToLowerCase()
    {
        Assert.Equal(ObjectId, SeedPartitionKeys.ForUser(Principal(("oid", ObjectId.ToUpperInvariant()))));
    }

    [Fact]
    public void MissingClaim_Throws()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var error = Assert.Throws<InvalidOperationException>(() => SeedPartitionKeys.ForUser(anonymous));
        Assert.Contains("oid", error.Message);
        Assert.Null(SeedPartitionKeys.TryForUser(anonymous));
        Assert.Null(SeedPartitionKeys.TryForTenant(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("../other")]
    public void NonGuidClaim_IsNoPartitionKey(string value)
    {
        Assert.Null(SeedPartitionKeys.TryForUser(Principal(("oid", value))));
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Bearer"));
}
