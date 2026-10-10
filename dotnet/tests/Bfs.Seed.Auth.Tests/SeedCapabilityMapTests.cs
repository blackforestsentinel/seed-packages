namespace Bfs.Seed.Auth.Tests;

public class SeedCapabilityMapTests
{
    internal const string ProjectYaml = """
        project: kundenportal
        features:
          sso: true
        auth:
          roles:
            Reader:
              description: Liest Rechnungen
              capabilities: [invoices.read]
            Admin:
              memberTypes: [User, Application]
              capabilities:
                - invoices.read
                - invoices.write
            Sync:
              description: Dienst für den nächtlichen Abgleich
              memberTypes: [Application]
              capabilities: [invoices.sync]
          assignmentRequired: false
        """;

    [Fact]
    public void Parse_ReadsRolesWithDefaults()
    {
        var map = SeedCapabilityMap.Parse(ProjectYaml);

        Assert.Equal(["Admin", "Reader", "Sync"], map.Roles.Keys.Order());
        Assert.Equal(SeedMemberTypes.User, map.Roles["Reader"].MemberTypes);
        Assert.Equal("Admin", map.Roles["Admin"].Description);
        Assert.Equal(SeedMemberTypes.User | SeedMemberTypes.Application, map.Roles["Admin"].MemberTypes);
        Assert.Equal(["invoices.read", "invoices.sync", "invoices.write"], map.Capabilities.Order());
    }

    [Fact]
    public void Resolve_CombinesRolesAndRespectsMemberTypes()
    {
        var map = SeedCapabilityMap.Parse(ProjectYaml);

        Assert.Equal(["invoices.read", "invoices.write"], map.Resolve(["Reader", "Admin", "Unbekannt"], SeedMemberTypes.User).Order());
        Assert.Empty(map.Resolve(["Sync"], SeedMemberTypes.User));
        Assert.Equal(["invoices.sync"], map.Resolve(["Sync", "Reader"], SeedMemberTypes.Application));
    }

    [Fact]
    public void AllowsApplication_OnlyForApplicationRoles()
    {
        var map = SeedCapabilityMap.Parse(ProjectYaml);

        Assert.True(map.AllowsApplication(["Sync"]));
        Assert.True(map.AllowsApplication(["Reader", "Admin"]));
        Assert.False(map.AllowsApplication(["Reader"]));
        Assert.False(map.AllowsApplication([]));
    }

    [Theory]
    [InlineData("project: x")]
    [InlineData("project: x\nauth:\n  assignmentRequired: true")]
    [InlineData("")]
    public void Parse_WithoutRoles_IsEmpty(string yaml)
    {
        var map = SeedCapabilityMap.Parse(yaml);

        Assert.Empty(map.Roles);
        Assert.Empty(map.Capabilities);
    }

    [Theory]
    [InlineData("auth:\n  roles: [Admin]", "auth.roles")]
    [InlineData("auth:\n  roles:\n    Admin:\n      capabilities: invoices.read", "auth.roles.Admin.capabilities")]
    [InlineData("auth:\n  roles:\n    Admin:\n      capabilities: [\"invoices read\"]", "invoices read")]
    [InlineData("auth:\n  roles:\n    Admin:\n      memberTypes: [Users]", "Users")]
    [InlineData("auth:\n  roles:\n    Admin:\n      memberTypes: []", "auth.roles.Admin.memberTypes")]
    [InlineData("auth:\n  roles:\n    \"Team Lead\": {}", "Leerzeichen")]
    [InlineData("auth: [", "kein gültiges YAML")]
    public void Parse_InvalidSection_ExplainsTheProblem(string yaml, string expected)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SeedCapabilityMap.Parse(yaml));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        var map = SeedCapabilityMap.Load(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.yaml"));

        Assert.Empty(map.Roles);
        Assert.Null(map.Source);
    }

    [Fact]
    public void FindUnknownCapabilities_ReportsTyposOnMethodsAndClasses()
    {
        var map = SeedCapabilityMap.Parse(ProjectYaml);

        var unknown = SeedCapabilityCheck.FindUnknownCapabilities(map, typeof(CapabilityFunctions).Assembly);

        Assert.Equal(
            [
                $"{typeof(CapabilityFunctions).FullName}.{nameof(CapabilityFunctions.Delete)}: invoices.delete",
                $"{typeof(TypoFunctions).FullName}: invoices.wirte",
            ],
            unknown.Order(StringComparer.Ordinal));
    }
}
