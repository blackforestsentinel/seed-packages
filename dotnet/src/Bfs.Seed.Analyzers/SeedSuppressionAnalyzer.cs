using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bfs.Seed.Analyzers;

/// <summary>
/// SEEDSUPPRESS001 und SEEDSUPPRESS002: Eine Meldung abzuschalten ist erlaubt, aber nur mit einer
/// Begründung von mindestens <see cref="SeedDiagnostics.MinimumJustificationLength"/> Zeichen, die
/// im Code steht und im Pull Request sichtbar ist.
/// </summary>
/// <remarks>
/// Generierten Code lässt dieser Analyzer aus: Generatoren schalten Warnungen oft pauschal ab, und
/// die Datei schreibt niemand von Hand. Die Regeln zur Anmeldung prüfen ihn trotzdem.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SeedSuppressionAnalyzer : DiagnosticAnalyzer
{
    private const string CodeAnalysisNamespace = "System.Diagnostics.CodeAnalysis";

    // Namen, unter denen die Attribute im Code stehen können; Aliasse kommen je Kompilierung dazu.
    private static readonly ImmutableHashSet<string> AttributeNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "SuppressMessage",
        "SuppressMessageAttribute",
        "UnconditionalSuppressMessage",
        "UnconditionalSuppressMessageAttribute");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SeedDiagnostics.SuppressionWithoutJustification, SeedDiagnostics.PragmaWithoutJustification);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var suppress = start.Compilation.GetTypeByMetadataName($"{CodeAnalysisNamespace}.SuppressMessageAttribute");
            var unconditional = start.Compilation.GetTypeByMetadataName($"{CodeAnalysisNamespace}.UnconditionalSuppressMessageAttribute");
            var names = AttributeNames.Union(Aliases(start.Compilation, start.CancellationToken));
            start.RegisterSyntaxNodeAction(c => AnalyzeAttribute(c, names, suppress, unconditional), SyntaxKind.Attribute);
        });
        context.RegisterSyntaxTreeAction(AnalyzePragmas);
    }

    private static void AnalyzeAttribute(
        SyntaxNodeAnalysisContext context, ImmutableHashSet<string> names, INamedTypeSymbol? suppress, INamedTypeSymbol? unconditional)
    {
        var attribute = (AttributeSyntax)context.Node;

        // Vorfilter am Namen: Nur diese Attribute brauchen das Semantikmodell.
        if (!names.Contains(RightmostName(attribute.Name)))
        {
            return;
        }

        var type = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol?.ContainingType;
        if (type is null || !(IsType(type, suppress, "SuppressMessageAttribute") || IsType(type, unconditional, "UnconditionalSuppressMessageAttribute")))
        {
            return;
        }

        string? justification = null;
        string? checkId = null;
        var position = 0;
        foreach (var argument in attribute.ArgumentList?.Arguments ?? default)
        {
            if (argument.NameEquals is { } named)
            {
                if (named.Name.Identifier.ValueText == "Justification")
                {
                    justification = Constant(context, argument.Expression);
                }

                continue;
            }

            // SuppressMessage(category, checkId), auch mit Namen: checkId: "CA1822".
            if (argument.NameColon?.Name.Identifier.ValueText == "checkId" || (argument.NameColon is null && position == 1))
            {
                checkId = Constant(context, argument.Expression);
            }

            position++;
        }

        if (IsJustification(justification))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SeedDiagnostics.SuppressionWithoutJustification,
            attribute.GetLocation(),
            type.Name.Substring(0, type.Name.Length - "Attribute".Length),
            string.IsNullOrWhiteSpace(checkId) ? "?" : checkId));
    }

    private static void AnalyzePragmas(SyntaxTreeAnalysisContext context)
    {
        if (context.Tree.GetRoot(context.CancellationToken) is not CompilationUnitSyntax root || !root.ContainsDirectives)
        {
            return;
        }

        for (var directive = root.GetFirstDirective(IsPragmaWarning); directive is not null; directive = directive.GetNextDirective(IsPragmaWarning))
        {
            var pragma = (PragmaWarningDirectiveTriviaSyntax)directive;
            if (!pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword) || HasJustification(pragma))
            {
                continue;
            }

            // Mit führendem Leerzeichen, damit die Meldung auch ohne IDs lesbar bleibt.
            var codes = pragma.ErrorCodes.Count == 0
                ? string.Empty
                : " " + string.Join(", ", pragma.ErrorCodes.Select(c => c.ToString().Trim()));
            context.ReportDiagnostic(Diagnostic.Create(SeedDiagnostics.PragmaWithoutJustification, pragma.GetLocation(), codes));
        }
    }

    private static bool IsPragmaWarning(DirectiveTriviaSyntax directive) => directive.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia);

    /// <summary>
    /// Ob in der Direktive ein Kommentar mit Begründung steht. Eine Direktive endet mit der Zeile, ein
    /// Kommentar darin steht also immer in derselben Zeile. Nach einer Direktive erlaubt C# nur
    /// <c>//</c>-Kommentare; <c>/* … */</c> ist dort kein Kommentar (Warnung CS1696).
    /// </summary>
    private static bool HasJustification(PragmaWarningDirectiveTriviaSyntax pragma)
    {
        foreach (var trivia in pragma.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                return IsJustification(trivia.ToString().Substring(2));
            }
        }

        return false;
    }

    // Mindestlänge ohne Leerraum am Rand. Der Platzhalter <Pending>, den Visual Studio beim
    // Unterdrücken per Glühbirne einträgt, ist kürzer und fällt damit von selbst heraus.
    private static bool IsJustification(string? text) =>
        text is not null && text.Trim().Length >= SeedDiagnostics.MinimumJustificationLength;

    private static bool IsType(INamedTypeSymbol type, INamedTypeSymbol? known, string name) =>
        known is not null
            ? SymbolEqualityComparer.Default.Equals(type, known)
            : type.Name == name && type.ContainingNamespace?.ToDisplayString() == CodeAnalysisNamespace;

    private static string RightmostName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => string.Empty,
    };

    /// <summary>
    /// Namen aller using-Aliasse der Kompilierung, auch globale aus anderen Dateien. Sonst entginge
    /// <c>using SM = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;</c> dem Vorfilter.
    /// Aliasse stehen nur am Dateianfang und in Namespaces, nicht in Typen.
    /// </summary>
    private static IEnumerable<string> Aliases(Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.GetRoot(cancellationToken) is not CompilationUnitSyntax root)
            {
                continue;
            }

            foreach (var name in Aliases(root.Usings, root.Members))
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<string> Aliases(SyntaxList<UsingDirectiveSyntax> usings, SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var directive in usings)
        {
            if (directive.Alias is { } alias)
            {
                yield return alias.Name.Identifier.ValueText;
            }
        }

        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax ns)
            {
                foreach (var name in Aliases(ns.Usings, ns.Members))
                {
                    yield return name;
                }
            }
        }
    }

    private static string? Constant(SyntaxNodeAnalysisContext context, ExpressionSyntax expression) =>
        context.SemanticModel.GetConstantValue(expression, context.CancellationToken) is { HasValue: true, Value: string value } ? value : null;
}
