using System.Security.Claims;
using System.Text.Json;

namespace Bfs.Seed.Auth.Tests;

public class SeedUserInfoTests
{
    private static readonly SeedCapabilityMap Map = SeedCapabilityMap.Parse(SeedCapabilityMapTests.ProjectYaml);

    [Fact]
    public void RoleDetails_FollowRolesWithDisplayNames()
    {
        var options = new SeedAuthOptions { LocalUser = { Roles = ["Reader", "Admin"] } };

        var info = SeedUserInfo.From(SeedLocalPrincipal.Create(options, Map));

        Assert.Equal(["Admin", "Reader"], info.Roles);
        Assert.Equal([new("Admin", "Administrator"), new SeedRoleDetail("Reader", "Reader")], info.RoleDetails);
    }

    [Fact]
    public void RoleDetails_WithoutDisplayNameClaims_UseTheValue()
    {
        // Etwa ein Principal, den ein Test oder eine eigene Middleware ohne Token-Prüfung baut.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(SeedClaimTypes.Role, "Reader"), new Claim(SeedClaimTypes.Role, "Admin")],
            "test"));

        var info = SeedUserInfo.From(principal);

        Assert.Equal([new("Admin", "Admin"), new SeedRoleDetail("Reader", "Reader")], info.RoleDetails);
        Assert.Empty(SeedUserInfo.From(new ClaimsPrincipal()).RoleDetails);
    }

    [Fact]
    public void RoleDetails_WithPositionalConstructor_FallBackToRoles()
    {
        var info = new SeedUserInfo("Erika Muster", null, null, null, ["Reader"], []);

        Assert.Equal([new SeedRoleDetail("Reader", "Reader")], info.RoleDetails);
    }

    [Fact]
    public void Json_KeepsExistingFieldsAndAddsRoleDetails()
    {
        var options = new SeedAuthOptions { LocalUser = { Roles = ["Reader", "Admin"] } };
        var info = SeedUserInfo.From(SeedLocalPrincipal.Create(options, Map));

        // So serialisiert OkObjectResult in der Function (ASP.NET Core, Web-Defaults).
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            """
            {"name":"Lokale Entwicklung","username":"dev@localhost","objectId":"00000000-0000-0000-0000-00000000d001","tenantId":"00000000-0000-0000-0000-00000000d000","roles":["Admin","Reader"],"capabilities":["invoices.read","invoices.write"],"roleDetails":[{"value":"Admin","displayName":"Administrator"},{"value":"Reader","displayName":"Reader"}]}
            """,
            json);
    }
}
