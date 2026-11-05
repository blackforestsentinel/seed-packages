using Microsoft.CodeAnalysis;

namespace Bfs.Seed.Analyzers;

/// <summary>Diagnosen der Seed-Analyzer. Die Hilfe je ID steht in der README von Bfs.Seed.Auth.</summary>
public static class SeedDiagnostics
{
    /// <summary>Kategorie der Regeln zur Anmeldung.</summary>
    public const string SecurityCategory = "Security";

    /// <summary>Kategorie der Regeln zu Unterdrückungen.</summary>
    public const string MaintainabilityCategory = "Maintainability";

    private const string Help = "https://github.com/blackforestsentinel/seed-packages/blob/main/dotnet/src/Bfs.Seed.Auth/README.md#";

    /// <summary>SEEDAUTH001: HTTP-Function ohne Berechtigungsangabe.</summary>
    public static readonly DiagnosticDescriptor FunctionWithoutAuthorization = new(
        id: "SEEDAUTH001",
        title: "HTTP-Function ohne Berechtigungsangabe",
        messageFormat: "Die HTTP-Function '{0}' legt nicht fest, wer sie aufrufen darf. Ergänze an Methode oder Klasse [RequireCapability(\"…\")], [RequireAnyRole], [AllowWithoutRole] oder [AllowAnonymous].",
        category: SecurityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ohne Angabe nimmt Bfs.Seed.Auth jedes gültige Token an, auch von Konten ohne App-Rolle; mit assignmentRequired: false hat jedes Konto des Tenants eines. " +
            "[RequireCapability] verlangt eine Capability, [RequireAnyRole] irgendeine App-Rolle, [AllowWithoutRole] gibt die Function bewusst für jedes angemeldete Konto frei und [AllowAnonymous] für Aufrufe ohne Token.",
        helpLinkUri: Help + "seedauth001");

    /// <summary>SEEDAUTH002: MCP-Werkzeug ohne Berechtigungsangabe.</summary>
    public static readonly DiagnosticDescriptor ToolWithoutAuthorization = new(
        id: "SEEDAUTH002",
        title: "MCP-Werkzeug ohne Berechtigungsangabe",
        messageFormat: "Das MCP-Werkzeug '{0}' legt nicht fest, wer es aufrufen darf. Ergänze an Methode oder Klasse [RequireCapability(\"…\")], [RequireAnyRole] oder [AllowWithoutRole]; [AllowAnonymous] gilt für Werkzeuge nicht.",
        category: SecurityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Bfs.Seed.Mcp zeigt ein Werkzeug ohne Angabe jedem, der sich am MCP-Endpunkt anmelden kann, auch ohne App-Rolle. " +
            "Der Endpunkt verlangt immer ein Token; ein Werkzeug ist deshalb nie anonym.",
        helpLinkUri: Help + "seedauth002");

    /// <summary>SEEDSUPPRESS001: [SuppressMessage] oder [UnconditionalSuppressMessage] ohne Begründung.</summary>
    /// <remarks>
    /// Nicht konfigurierbar: Weder Pragma noch [SuppressMessage], .editorconfig oder NoWarn schalten die
    /// Regel ab. Sonst ließe sich die Pflicht zur Begründung ihrerseits ohne Begründung abschalten.
    /// </remarks>
    public static readonly DiagnosticDescriptor SuppressionWithoutJustification = new(
        id: "SEEDSUPPRESS001",
        title: "Unterdrückung ohne Begründung",
        messageFormat: "[{0}] für '{1}' braucht eine Begründung. Ergänze Justification = \"…\" mit dem Grund, warum die Meldung hier nicht zutrifft.",
        category: MaintainabilityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Eine Ausnahme von einer Regel ist erlaubt, aber nur mit einer Begründung, die im Code und im Pull Request steht. " +
            "Leer, nur Leerzeichen oder der Platzhalter <Pending> aus Visual Studio gelten nicht als Begründung. Das gilt auch für [assembly: SuppressMessage] in GlobalSuppressions.cs.",
        helpLinkUri: Help + "seedsuppress001",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>SEEDSUPPRESS002: <c>#pragma warning disable</c> ohne Begründung in derselben Zeile.</summary>
    /// <remarks>Nicht konfigurierbar, aus demselben Grund wie <see cref="SuppressionWithoutJustification"/>.</remarks>
    public static readonly DiagnosticDescriptor PragmaWithoutJustification = new(
        id: "SEEDSUPPRESS002",
        title: "#pragma warning disable ohne Begründung",
        messageFormat: "'#pragma warning disable{0}' braucht eine Begründung als Kommentar in derselben Zeile, etwa '#pragma warning disable{0} // Grund'",
        category: MaintainabilityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Eine Ausnahme von einer Regel ist erlaubt, aber nur mit einer Begründung, die im Code und im Pull Request steht. " +
            "Der Kommentar muss in derselben Zeile wie die Direktive stehen und Text enthalten; #pragma warning restore braucht keinen.",
        helpLinkUri: Help + "seedsuppress002",
        customTags: WellKnownDiagnosticTags.NotConfigurable);
}
