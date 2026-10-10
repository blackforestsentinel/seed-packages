using Microsoft.AspNetCore.Authorization;

namespace Bfs.Seed.Auth.Tests;

// Functions für die Tests der Attribute. Delete und TypoFunctions nennen absichtlich Capabilities,
// die SeedCapabilityMapTests.ProjectYaml nicht kennt.

public sealed class CapabilityFunctions
{
    public string Me() => "me";

    [RequireCapability("invoices.read")]
    public string List() => "list";

    [RequireCapability("invoices.read")]
    [RequireCapability("invoices.write")]
    public string Update() => "update";

    [RequireCapability("invoices.delete")]
    public string Delete() => "delete";

    [AllowAnonymous]
    public string Health() => "ok";
}

[RequireCapability("invoices.wirte")]
public sealed class TypoFunctions
{
    public string Run() => "run";
}

[RequireScope("mcp_access", "access_as_user")]
[RequireCapability("invoices.read")]
public sealed class ScopedFunctions
{
    public string Tools() => "tools";

    [RequireScope("special_scope")]
    [RequireCapability("invoices.write")]
    public string Special() => "special";
}
