using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bfs.Seed.Analyzers;

/// <summary>
/// SEEDSUPPRESS001 und SEEDSUPPRESS002: Eine Meldung abzuschalten ist erlaubt, aber nur mit einer
/// Begründung, die im Code steht und im Pull Request sichtbar ist.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SeedSuppressionAnalyzer : DiagnosticAnalyzer
{
    private const string CodeAnalysisNamespace = "System.Diagnostics.CodeAnalysis";

    // Visual Studio trägt diesen Text ein, wenn man eine Meldung per Glühbirne unterdrückt.
    private const string VisualStudioPlaceholder = "<Pending>";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SeedDiagnostics.SuppressionWithoutJustification, SeedDiagnostics.PragmaWithoutJustification);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxTreeAction(AnalyzePragmas);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
        var type = symbol?.ContainingType;
        if (type is null
            || type.ContainingNamespace?.ToDisplayString() != CodeAnalysisNamespace
            || (type.Name != "SuppressMessageAttribute" && type.Name != "UnconditionalSuppressMessageAttribute"))
        {
            return;
        }

        var arguments = attribute.ArgumentList?.Arguments ?? default;
        string? justification = null;
        string? checkId = null;
        var position = 0;
        foreach (var argument in arguments)
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
        var root = context.Tree.GetRoot(context.CancellationToken);
        if (!root.ContainsDirectives)
        {
            return;
        }

        foreach (var trivia in root.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia)
                || trivia.GetStructure() is not PragmaWarningDirectiveTriviaSyntax pragma
                || !pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)
                || HasComment(pragma))
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

    /// <summary>
    /// Ob in der Direktive ein Kommentar mit Text steht. Eine Direktive endet mit der Zeile, ein
    /// Kommentar darin steht also immer in derselben Zeile. Nach einer Direktive erlaubt C# nur
    /// <c>//</c>-Kommentare; <c>/* … */</c> ist dort kein Kommentar (Warnung CS1696).
    /// </summary>
    private static bool HasComment(PragmaWarningDirectiveTriviaSyntax pragma)
    {
        foreach (var trivia in pragma.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.ToString().Substring(2).Any(char.IsLetter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJustification(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && !string.Equals(text!.Trim(), VisualStudioPlaceholder, StringComparison.OrdinalIgnoreCase);

    private static string? Constant(SyntaxNodeAnalysisContext context, ExpressionSyntax expression) =>
        context.SemanticModel.GetConstantValue(expression, context.CancellationToken) is { HasValue: true, Value: string value } ? value : null;
}
