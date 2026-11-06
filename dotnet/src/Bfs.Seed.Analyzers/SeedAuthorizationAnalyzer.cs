using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bfs.Seed.Analyzers;

/// <summary>
/// SEEDAUTH001 bis SEEDAUTH003: Jede HTTP-Function und jedes MCP-Werkzeug legt ausdrücklich fest,
/// wer es aufrufen darf, und <c>[AllowAnonymous]</c> hebt keine andere Angabe still auf. Ohne
/// Angabe lässt Bfs.Seed.Auth jedes gültige Token durch, auch von Konten ohne App-Rolle; das soll
/// eine bewusste Entscheidung sein, kein Versehen.
/// </summary>
/// <remarks>
/// Die Attribute zählen an Methode und Klasse, auch geerbt, wie in der Middleware von Bfs.Seed.Auth
/// (<c>GetCustomAttributes(inherit: true)</c>). Generierter Code wird mit geprüft: Sonst ließe sich
/// eine Function mit <c>// &lt;auto-generated/&gt;</c> oder <c>generated_code = true</c> vorbeischleusen.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SeedAuthorizationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        SeedDiagnostics.FunctionWithoutAuthorization,
        SeedDiagnostics.ToolWithoutAuthorization,
        SeedDiagnostics.AnonymousOverridesAuthorization);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var types = new KnownTypes(start.Compilation);

            // Nur in Projekten mit Bfs.Seed.Auth: Ohne die Token-Prüfung haben die Attribute keine Wirkung.
            if (!types.HasSeedAuth)
            {
                return;
            }

            start.RegisterSymbolAction(c => AnalyzeMethod(c, types), SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, KnownTypes types)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || method.Locations.Length == 0)
        {
            return;
        }

        foreach (var attribute in method.GetAttributes())
        {
            if (types.Function.Is(attribute.AttributeClass))
            {
                // Timer, Queue und andere Trigger ruft niemand von außen auf.
                if (HasHttpTrigger(method, types))
                {
                    AnalyzeFunction(context, method, FunctionName(attribute, method), Collect(method, types));
                }

                return;
            }

            // Werkzeuge prüft nur Bfs.Seed.Mcp auf Capabilities. Ein eigener MCP-Server mit dem SDK
            // kennt die Seed-Attribute nicht; dort wäre die Meldung irreführend.
            if (types.HasSeedMcp && types.McpServerTool.Is(attribute.AttributeClass))
            {
                // Der MCP-Endpunkt verlangt immer ein Token und wertet [AllowAnonymous] an Werkzeugen
                // nicht aus. Es gibt also weder ein anonymes Werkzeug noch einen Widerspruch (SEEDAUTH003).
                if ((Collect(method, types) & Markers.Authorization) == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SeedDiagnostics.ToolWithoutAuthorization, method.Locations[0], ToolName(attribute, method)));
                }

                return;
            }
        }
    }

    private static void AnalyzeFunction(SymbolAnalysisContext context, IMethodSymbol method, string name, Markers markers)
    {
        if ((markers & Markers.Anonymous) != 0)
        {
            // Die Middleware fragt IAllowAnonymous zuerst ab und übergeht dann alles andere.
            var overridden = Names(markers & Markers.Overridable);
            if (overridden.Length > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SeedDiagnostics.AnonymousOverridesAuthorization, method.Locations[0], name, overridden));
            }

            return;
        }

        if ((markers & Markers.Authorization) == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(SeedDiagnostics.FunctionWithoutAuthorization, method.Locations[0], name));
        }
    }

    private static bool HasHttpTrigger(IMethodSymbol method, KnownTypes types)
    {
        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (types.HttpTrigger.Is(attribute.AttributeClass))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Welche Angaben Methode und Klasse machen, auch geerbt.</summary>
    private static Markers Collect(IMethodSymbol method, KnownTypes types)
    {
        var markers = Markers.None;
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            markers |= Collect(current.GetAttributes(), types);
        }

        for (var type = method.ContainingType; type is not null; type = type.BaseType)
        {
            markers |= Collect(type.GetAttributes(), types);
        }

        return markers;
    }

    private static Markers Collect(ImmutableArray<AttributeData> attributes, KnownTypes types)
    {
        var markers = Markers.None;
        foreach (var attribute in attributes)
        {
            var type = attribute.AttributeClass;
            if (type is null)
            {
                continue;
            }

            if (types.RequireCapability.Is(type))
            {
                markers |= Markers.Capability;
            }
            else if (types.RequireAnyRole.Is(type))
            {
                markers |= Markers.AnyRole;
            }
            else if (types.AllowWithoutRole.Is(type))
            {
                markers |= Markers.WithoutRole;
            }
            else if (types.RequireScope.Is(type))
            {
                markers |= Markers.Scope;
            }
            else if (types.IsAllowAnonymous(type))
            {
                markers |= Markers.Anonymous;
            }
        }

        return markers;
    }

    private static string Names(Markers markers)
    {
        var names = new List<string>();
        if ((markers & Markers.Capability) != 0)
        {
            names.Add("RequireCapability");
        }

        if ((markers & Markers.AnyRole) != 0)
        {
            names.Add("RequireAnyRole");
        }

        if ((markers & Markers.WithoutRole) != 0)
        {
            names.Add("AllowWithoutRole");
        }

        if ((markers & Markers.Scope) != 0)
        {
            names.Add("RequireScope");
        }

        return string.Join("], [", names);
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

    [Flags]
    private enum Markers
    {
        None = 0,
        Capability = 1,
        AnyRole = 2,
        WithoutRole = 4,
        Scope = 8,
        Anonymous = 16,

        /// <summary>Angaben, die festlegen, wer aufrufen darf (ohne [AllowAnonymous]).</summary>
        Authorization = Capability | AnyRole | WithoutRole,

        /// <summary>Angaben, die [AllowAnonymous] still aufhebt.</summary>
        Overridable = Capability | AnyRole | WithoutRole | Scope,
    }

    /// <summary>Die Typen der Attribute, einmal je Kompilierung aufgelöst.</summary>
    private sealed class KnownTypes
    {
        private const string Auth = "Bfs.Seed.Auth";

        private readonly KnownType allowAnonymousInterface;

        public KnownTypes(Compilation compilation)
        {
            RequireCapability = new(compilation, Auth, "RequireCapabilityAttribute");
            RequireAnyRole = new(compilation, Auth, "RequireAnyRoleAttribute");
            AllowWithoutRole = new(compilation, Auth, "AllowWithoutRoleAttribute");
            RequireScope = new(compilation, Auth, "RequireScopeAttribute");
            Function = new(compilation, "Microsoft.Azure.Functions.Worker", "FunctionAttribute");
            HttpTrigger = new(compilation, "Microsoft.Azure.Functions.Worker", "HttpTriggerAttribute");
            McpServerTool = new(compilation, "ModelContextProtocol.Server", "McpServerToolAttribute");
            allowAnonymousInterface = new(compilation, "Microsoft.AspNetCore.Authorization", "IAllowAnonymous");
            HasSeedAuth = RequireCapability.Symbol is not null;
            HasSeedMcp = compilation.GetTypeByMetadataName("Bfs.Seed.Mcp.SeedMcpBuilder") is not null;
        }

        public bool HasSeedAuth { get; }

        public bool HasSeedMcp { get; }

        public KnownType RequireCapability { get; }

        public KnownType RequireAnyRole { get; }

        public KnownType AllowWithoutRole { get; }

        public KnownType RequireScope { get; }

        public KnownType Function { get; }

        public KnownType HttpTrigger { get; }

        public KnownType McpServerTool { get; }

        /// <summary>Wie die Middleware: jedes Attribut, das IAllowAnonymous umsetzt.</summary>
        public bool IsAllowAnonymous(INamedTypeSymbol type)
        {
            foreach (var implemented in type.AllInterfaces)
            {
                if (allowAnonymousInterface.Is(implemented))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Ein Typ, verglichen über sein Symbol. Findet die Kompilierung ihn nicht eindeutig, etwa weil
    /// zwei Assemblies ihn definieren, über Namespace und Namen.
    /// </summary>
    private sealed class KnownType(Compilation compilation, string ns, string name)
    {
        public INamedTypeSymbol? Symbol { get; } = compilation.GetTypeByMetadataName($"{ns}.{name}");

        public bool Is(ITypeSymbol? type) =>
            type is not null
            && (Symbol is not null
                ? SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, Symbol)
                : type.Name == name && type.ContainingNamespace?.ToDisplayString() == ns);
    }
}
