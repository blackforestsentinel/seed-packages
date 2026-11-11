using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.Functions.Worker;

namespace Bfs.Seed.Auth.Tests;

// Functions für die Tests der Attribute. Delete und TypoFunctions nennen absichtlich Capabilities,
// die SeedCapabilityMapTests.ProjectYaml nicht kennt. Die Middleware findet die Methode über
// [Function]; die Tests übergeben ohne eigenen Namen den Methodennamen als Namen der Function.

public sealed class CapabilityFunctions
{
    [Function(nameof(Me))]
    public string Me() => "me";

    [Function(nameof(List))]
    [RequireCapability("invoices.read")]
    public string List() => "list";

    [Function(nameof(Update))]
    [RequireCapability("invoices.read")]
    [RequireCapability("invoices.write")]
    public string Update() => "update";

    [Function(nameof(Delete))]
    [RequireCapability("invoices.delete")]
    public string Delete() => "delete";

    [Function(nameof(Health))]
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
    [Function(nameof(Tools))]
    public string Tools() => "tools";

    [Function(nameof(Special))]
    [RequireScope("special_scope")]
    [RequireCapability("invoices.write")]
    public string Special() => "special";
}

public sealed class RoleFunctions
{
    [Function(nameof(Orders))]
    [RequireAnyRole]
    public string Orders() => "orders";

    [Function(nameof(Me))]
    [AllowWithoutRole]
    public string Me() => "me";
}

[RequireAnyRole]
public sealed class RoleClassFunctions
{
    // [AllowWithoutRole] lockert nichts: Die Klasse verlangt weiter eine Rolle.
    [Function(nameof(Run))]
    [AllowWithoutRole]
    public string Run() => "run";
}

// Drei Functions mit demselben Methodennamen: Der Entry Point ist für alle SameNameFunctions.Run,
// nur der Name im [Function]-Attribut unterscheidet sie.
public sealed class SameNameFunctions
{
    [Function("SameNameInvoices")]
    [RequireCapability("invoices.write")]
    public string Run() => "invoices";

    [Function("SameNameStatus")]
    [AllowAnonymous]
    public string Run(int attempt) => $"status {attempt}";

    [Function("SameNameQueue")]
    public string Run(string message) => message;
}

// Methode ohne [Function]: Die Middleware rät nicht, welche Anforderungen gelten.
public sealed class UnmarkedFunctions
{
    [RequireCapability("invoices.write")]
    public string Run() => "run";
}
