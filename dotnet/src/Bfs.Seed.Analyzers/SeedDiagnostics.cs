using Microsoft.CodeAnalysis;

namespace Bfs.Seed.Analyzers;

/// <summary>
/// Diagnosen der Seed-Analyzer. Die Hilfe je ID steht in der README von Bfs.Seed.Auth.
/// </summary>
/// <remarks>
/// Alle Regeln sind Fehler und nicht konfigurierbar: Weder <c>#pragma</c> noch <c>NoWarn</c>,
/// <c>.editorconfig</c> oder ein Ruleset schalten sie ab. Eine Ausnahme geht nur über
/// <c>[SuppressMessage]</c> mit Begründung, damit sie im Code und im Pull Request steht.
/// </remarks>
public static class SeedDiagnostics
{
    /// <summary>Kategorie der Regeln zur Anmeldung.</summary>
    public const string SecurityCategory = "Security";

    /// <summary>Kategorie der Regeln zu Unterdrückungen.</summary>
    public const string MaintainabilityCategory = "Maintainability";

    /// <summary>
    /// Mindestlänge einer Begründung in Zeichen, ohne Leerraum am Rand. Dieselbe Grenze gilt in
    /// seed-pipelines für <c>reason</c> in project.yaml und für Ausnahmen des Terraform-Scans.
    /// </summary>
    public const int MinimumJustificationLength = 10;

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
        helpLinkUri: Help + "seedauth001",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

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
        helpLinkUri: Help + "seedauth002",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>SEEDAUTH003: [AllowAnonymous] zusammen mit einer Berechtigungsangabe, die dann nicht wirkt.</summary>
    public static readonly DiagnosticDescriptor AnonymousOverridesAuthorization = new(
        id: "SEEDAUTH003",
        title: "[AllowAnonymous] hebt andere Berechtigungsangaben auf",
        messageFormat: "Die HTTP-Function '{0}' ist durch [AllowAnonymous] ohne Anmeldung erreichbar; [{1}] prüft die API deshalb nicht. Entferne eines davon; gilt [AllowAnonymous] an der Klasse nur einer Function, setze es an deren Methode.",
        category: SecurityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Die Token-Prüfung übergeht eine Function mit [AllowAnonymous] an Methode oder Klasse ganz. " +
            "[RequireCapability], [RequireAnyRole], [AllowWithoutRole] und [RequireScope] prüft sie dann nicht, auch wenn sie an der Methode stehen.",
        helpLinkUri: Help + "seedauth003",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>SEEDAUTH004: Mehrere Functions einer Klasse mit demselben Methodennamen.</summary>
    public static readonly DiagnosticDescriptor FunctionsShareMethodName = new(
        id: "SEEDAUTH004",
        title: "Mehrere Functions mit demselben Methodennamen",
        messageFormat: "Die Functions {0} in '{1}' teilen sich den Methodennamen '{2}'. Gib jeder Function eine Methode mit eigenem Namen.",
        category: SecurityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Der Entry Point einer Function nennt nur Klasse und Methode, für überladene Methoden also für alle denselben. " +
            "Was Functions über den Entry Point zuordnet, etwa ältere Versionen von Bfs.Seed.Auth, verwechselt sie und wendet die Berechtigungsangaben der einen auf die andere an.",
        helpLinkUri: Help + "seedauth004",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>SEEDSUPPRESS001: [SuppressMessage] oder [UnconditionalSuppressMessage] ohne ausreichende Begründung.</summary>
    public static readonly DiagnosticDescriptor SuppressionWithoutJustification = new(
        id: "SEEDSUPPRESS001",
        title: "Unterdrückung ohne Begründung",
        messageFormat: "[{0}] für '{1}' braucht eine Begründung mit mindestens 10 Zeichen. Ergänze Justification = \"…\" mit dem Grund, warum die Meldung hier nicht zutrifft.",
        category: MaintainabilityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Eine Ausnahme von einer Regel ist erlaubt, aber nur mit einer Begründung, die im Code und im Pull Request steht. " +
            "Sie hat mindestens 10 Zeichen ohne Leerraum am Rand; der Platzhalter <Pending> aus Visual Studio genügt nicht. Das gilt auch für [assembly: SuppressMessage] in GlobalSuppressions.cs.",
        helpLinkUri: Help + "seedsuppress001",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>SEEDSUPPRESS002: <c>#pragma warning disable</c> ohne ausreichende Begründung in derselben Zeile.</summary>
    public static readonly DiagnosticDescriptor PragmaWithoutJustification = new(
        id: "SEEDSUPPRESS002",
        title: "#pragma warning disable ohne Begründung",
        messageFormat: "'#pragma warning disable{0}' braucht eine Begründung mit mindestens 10 Zeichen als Kommentar in derselben Zeile, etwa '#pragma warning disable{0} // Grund der Ausnahme'",
        category: MaintainabilityCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Eine Ausnahme von einer Regel ist erlaubt, aber nur mit einer Begründung, die im Code und im Pull Request steht. " +
            "Der //-Kommentar steht in derselben Zeile wie die Direktive und hat mindestens 10 Zeichen ohne Leerraum am Rand; #pragma warning restore braucht keinen.",
        helpLinkUri: Help + "seedsuppress002",
        customTags: WellKnownDiagnosticTags.NotConfigurable);
}
