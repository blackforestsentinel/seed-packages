using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bfs.Seed.Analyzers;

/// <summary>
/// SEEDAUTH001 und SEEDAUTH002: Jede HTTP-Function und jedes MCP-Werkzeug legt ausdrücklich fest,
/// wer es aufrufen darf. Ohne Angabe lässt Bfs.Seed.Auth jedes gültige Token durch, auch von Konten
/// ohne App-Rolle; das soll eine bewusste Entscheidung sein, kein Versehen.
/// </summary>
/// <remarks>
/// Die Attribute zählen an Methode und Klasse, auch geerbt, wie in der Middleware von Bfs.Seed.Auth
/// (<c>GetCustomAttributes(inherit: true)</c>). Typen werden über ihren Namen erkannt, damit der
/// Analyzer nicht von einer bestimmten Version der Functions- oder MCP-Pakete abhängt.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SeedAuthorizationAnalyzer : DiagnosticAnalyzer
{
    private const string AuthNamespace = "Bfs.Seed.Auth";
    private const string FunctionsNamespace = "Microsoft.Azure.Functions.Worker";
    private const string McpNamespace = "ModelContextProtocol.Server";
    private const string AspNetAuthorizationNamespace = "Microsoft.AspNetCore.Authorization";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SeedDiagnostics.FunctionWithoutAuthorization, SeedDiagnostics.ToolWithoutAuthorization);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            // Nur in Projekten mit Bfs.Seed.Auth: Ohne die Token-Prüfung haben die Attribute keine Wirkung.
            if (start.Compilation.GetTypeByMetadataName($"{AuthNamespace}.RequireCapabilityAttribute") is null)
            {
                return;
            }

            // Werkzeuge prüft nur Bfs.Seed.Mcp auf Capabilities. Ein eigener MCP-Server mit dem SDK
            // kennt die Seed-Attribute nicht; dort wäre die Meldung irreführend.
            var checkTools = start.Compilation.GetTypeByMetadataName("Bfs.Seed.Mcp.SeedMcpBuilder") is not null;
            start.RegisterSymbolAction(c => AnalyzeMethod(c, checkTools), SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, bool checkTools)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || method.Locations.Length == 0)
        {
            return;
        }

        foreach (var attribute in method.GetAttributes())
        {
            if (Is(attribute.AttributeClass, FunctionsNamespace, "FunctionAttribute"))
            {
                // Timer, Queue und andere Trigger ruft niemand von außen auf.
                if (HasHttpTrigger(method) && !HasMarker(method, allowAnonymousCounts: true))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SeedDiagnostics.FunctionWithoutAuthorization, method.Locations[0], FunctionName(attribute, method)));
                }

                return;
            }

            if (checkTools && Is(attribute.AttributeClass, McpNamespace, "McpServerToolAttribute"))
            {
                // Der MCP-Endpunkt verlangt immer ein Token; [AllowAnonymous] gibt ein Werkzeug nicht frei.
                if (!HasMarker(method, allowAnonymousCounts: false))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SeedDiagnostics.ToolWithoutAuthorization, method.Locations[0], ToolName(attribute, method)));
                }

                return;
            }
        }
    }

    private static bool HasHttpTrigger(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (Is(attribute.AttributeClass, FunctionsNamespace, "HttpTriggerAttribute"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Ob Methode oder Klasse (auch geerbt) festlegen, wer aufrufen darf.</summary>
    private static bool HasMarker(IMethodSymbol method, bool allowAnonymousCounts)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            if (HasMarker(current.GetAttributes(), allowAnonymousCounts))
            {
                return true;
            }
        }

        for (var type = method.ContainingType; type is not null; type = type.BaseType)
        {
            if (HasMarker(type.GetAttributes(), allowAnonymousCounts))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMarker(ImmutableArray<AttributeData> attributes, bool allowAnonymousCounts)
    {
        foreach (var attribute in attributes)
        {
            var type = attribute.AttributeClass;
            if (Is(type, AuthNamespace, "RequireCapabilityAttribute")
                || Is(type, AuthNamespace, "RequireAnyRoleAttribute")
                || Is(type, AuthNamespace, "AllowWithoutRoleAttribute"))
            {
                return true;
            }

            // Wie die Middleware: jedes Attribut, das IAllowAnonymous umsetzt.
            if (allowAnonymousCounts && type is not null
                && type.AllInterfaces.Any(i => Is(i, AspNetAuthorizationNamespace, "IAllowAnonymous")))
            {
                return true;
            }
        }

        return false;
    }

    private static string FunctionName(AttributeData function, IMethodSymbol method) =>
        function.ConstructorArguments.Length > 0 && function.ConstructorArguments[0].Value is string name && name.Length > 0
            ? name
            : method.Name;

    private static string ToolName(AttributeData tool, IMethodSymbol method)
    {
        foreach (var argument in tool.NamedArguments)
        {
            if (argument.Key == "Name" && argument.Value.Value is string name && name.Length > 0)
            {
                return name;
            }
        }

        return method.Name;
    }

    private static bool Is(ITypeSymbol? type, string ns, string name) =>
        type is not null && type.Name == name && type.ContainingNamespace?.ToDisplayString() == ns;
}
