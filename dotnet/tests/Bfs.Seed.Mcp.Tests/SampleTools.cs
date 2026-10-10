using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Bfs.Seed.Auth;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Bfs.Seed.Mcp.Tests;

/// <summary>Werkzeuge für die Tests: eines frei, eines mit Capability, eines mit Argumenten.</summary>
[McpServerToolType]
public sealed class SampleTools(Greeter greeter)
{
    [McpServerTool(Name = "status", ReadOnly = true), Description("Liefert den Status.")]
    public static string Status() => "ok";

    [McpServerTool(Name = "rechnung_anlegen"), Description("Legt eine Rechnung an.")]
    [RequireCapability("invoices.write")]
    public string CreateInvoice(
        [Description("Kunde")] string kunde,
        [Description("Betrag in Euro")] decimal betrag,
        [Description("Art")] InvoiceKind art = InvoiceKind.Normal) => $"{kunde}: {betrag.ToString(CultureInfo.InvariantCulture)} ({art})";

    [McpServerTool(Name = "wer_bin_ich", ReadOnly = true), Description("Nennt die angemeldete Person.")]
    public string WhoAmI(ClaimsPrincipal user) => greeter.Greet(user.FindFirst("name")?.Value);
}

/// <summary>Werkzeugklasse mit Capability an der Klasse.</summary>
[McpServerToolType]
[RequireCapability("reports.read")]
public sealed class ReportTools
{
    [McpServerTool(Name = "bericht"), Description("Liefert einen Bericht.")]
    [RequireCapability("reports.export")]
    public static string Report([Description("Jahr")] int jahr, [Description("Monate")] int[]? monate = null) =>
        $"Bericht {jahr} ({monate?.Length ?? 0} Monate)";
}

/// <summary>Werkzeuge, die scheitern; nur für einzelne Tests registriert.</summary>
public sealed class FailingTools
{
    [McpServerTool(Name = "fachfehler"), Description("Scheitert mit einer Meldung für das Modell.")]
    public static string BusinessError() => throw new McpException("Kunde nicht gefunden.");

    [McpServerTool(Name = "absturz"), Description("Scheitert unerwartet.")]
    public static string Crash() => throw new InvalidOperationException("interne Einzelheit");
}

public enum InvoiceKind
{
    Normal,
    Gutschrift,
}

/// <summary>Ein Dienst aus der Dependency Injection.</summary>
public sealed class Greeter
{
    public string Greet(string? name) => $"Hallo {name ?? "unbekannt"}";
}
