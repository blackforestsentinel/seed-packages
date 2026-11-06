using Microsoft.CodeAnalysis;

namespace Bfs.Seed.Analyzers.Tests;

public class SeedSuppressionAnalyzerTests
{
    [Theory]
    [InlineData("""[SuppressMessage("Performance", "CA1822")]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "")]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "   ")]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "<Pending>")]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "x")]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "123456789")]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "   kurz ok   ")]""")]
    [InlineData("""[SuppressMessage("Performance", checkId: "CA1822", MessageId = "x")]""")]
    [InlineData("""[System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Performance", "CA1822")]""")]
    public async Task SuppressMessage_WithoutJustification_IsError(string attribute)
    {
        var diagnostics = await RunAsync($$"""
            using System.Diagnostics.CodeAnalysis;

            public sealed class Orders
            {
                {{attribute}}
                public int Count() => 0;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDSUPPRESS001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'CA1822'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "Öffentliche API, bleibt Instanzmethode.")]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = Reasons.PublicApi)]""")]
    [InlineData("""[SuppressMessage("Performance", "CA1822", Justification = "1234567890")]""")]
    [InlineData("""[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Typen bleiben über DynamicDependency erhalten.")]""")]
    public async Task Suppression_WithJustification_IsFine(string attribute)
    {
        var diagnostics = await RunAsync($$"""
            using System.Diagnostics.CodeAnalysis;

            public sealed class Orders
            {
                {{attribute}}
                public int Count() => 0;
            }

            internal static class Reasons
            {
                public const string PublicApi = "Öffentliche API, bleibt Instanzmethode.";
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UnconditionalSuppressMessage_WithoutJustification_IsError()
    {
        var diagnostics = await RunAsync("""
            using System.Diagnostics.CodeAnalysis;

            public sealed class Orders
            {
                [UnconditionalSuppressMessage("Trimming", "IL2026")]
                public int Count() => 0;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDSUPPRESS001", diagnostic.Id);
        Assert.StartsWith("[UnconditionalSuppressMessage] für 'IL2026'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobalSuppressions_WithoutJustification_IsError()
    {
        var diagnostics = await RunAsync("""
            // GlobalSuppressions.cs, wie Visual Studio sie anlegt
            using System.Diagnostics.CodeAnalysis;

            [assembly: SuppressMessage("Performance", "CA1822", Justification = "<Pending>", Scope = "member", Target = "~M:Orders.Count~System.Int32")]
            [assembly: SuppressMessage("Style", "IDE0005", Justification = "Generierter Code im Ordner Legacy.", Scope = "namespaceanddescendants", Target = "~N:Legacy")]

            public sealed class Orders
            {
                public int Count() => 0;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDSUPPRESS001", diagnostic.Id);
        Assert.Contains("'CA1822'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortJustification_ForSeedRule_IsError_AndNamesTheRule()
    {
        var diagnostics = await RunAsync("""
            using System.Diagnostics.CodeAnalysis;

            [assembly: SuppressMessage("Seed", "SEEDAUTH001", Justification = "x")]
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SEEDSUPPRESS001", diagnostic.Id);
        Assert.Equal(
            "[SuppressMessage] für 'SEEDAUTH001' braucht eine Begründung mit mindestens 10 Zeichen. Ergänze Justification = \"…\" mit dem Grund, warum die Meldung hier nicht zutrifft.",
            diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("using SM = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;", "", "")]
    [InlineData("", "using SM = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;", "")]
    [InlineData("", "", "global using SM = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;")]
    public async Task SuppressMessage_UnderAlias_IsFound(string onTop, string inNamespace, string otherFile)
    {
        // Der Vorfilter am Namen darf einen Alias nicht übersehen, auch nicht aus einer anderen Datei.
        var diagnostics = await AnalyzerRunner.RunAsync(new SeedSuppressionAnalyzer(), [
            $$"""
            {{onTop}}
            namespace Orders
            {
                {{inNamespace}}

                public sealed class Order
                {
                    [SM("Performance", "CA1822")]
                    public int Count() => 0;
                }
            }
            """,
            otherFile,
        ]);

        Assert.Equal("SEEDSUPPRESS001", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Rule_CanBeSwitchedOffOnlyWithJustification()
    {
        // Wie jede Ausnahme: per [SuppressMessage] mit Begründung, etwa für Altcode beim Umzug in Seed.
        var diagnostics = await RunAsync("""
            using System.Diagnostics.CodeAnalysis;

            [assembly: SuppressMessage("Maintainability", "SEEDSUPPRESS002", Justification = "Altcode, Begründungen folgen je Datei.")]

            public sealed class Orders
            {
                public void Count()
                {
            #pragma warning disable CS0168
                    int unused;
            #pragma warning restore CS0168
                }
            }
            """);

        Assert.Empty(diagnostics.Effective());
    }

    [Theory]
    [InlineData("#pragma warning disable CS0168", " CS0168")]
    [InlineData("#pragma warning disable CS0168, CS0219", " CS0168, CS0219")]
    [InlineData("#pragma warning disable CS0168 //", " CS0168")]
    [InlineData("#pragma warning disable CS0168 // ---", " CS0168")]
    [InlineData("#pragma warning disable CS0168 //Grund", " CS0168")]
    [InlineData("#pragma warning disable CS0168 //     123456789     ", " CS0168")]
    [InlineData("#pragma warning disable CS0168 /* Grund */", " CS0168")]
    [InlineData("#pragma warning disable", "")]
    public async Task Pragma_WithoutComment_IsError(string pragma, string codes)
    {
        var diagnostics = await RunAsync($$"""
            public sealed class Orders
            {
                public void Count()
                {
            {{pragma}}
                    int unused;
            #pragma warning restore CS0168
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Effective());
        Assert.Equal("SEEDSUPPRESS002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.StartsWith($"'#pragma warning disable{codes}' braucht", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#pragma warning disable CS0168 // Wird erst im nächsten Schritt gelesen.")]
    [InlineData("#pragma warning disable CS0168 //1234567890")]
    [InlineData("#pragma warning disable CS0168, CS0219 // Beispiel für die Doku")]
    public async Task Pragma_WithComment_IsFine(string pragma)
    {
        var diagnostics = await RunAsync($$"""
            public sealed class Orders
            {
                public void Count()
                {
            {{pragma}}
                    int unused;
            #pragma warning restore CS0168
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Pragma_CommentOnTheLineBefore_DoesNotCount()
    {
        var diagnostics = await RunAsync("""
            public sealed class Orders
            {
                public void Count()
                {
                    // Wird erst im nächsten Schritt gelesen.
            #pragma warning disable CS0168
                    int unused;
            #pragma warning restore CS0168
                }
            }
            """);

        Assert.Equal("SEEDSUPPRESS002", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Pragma_CannotSwitchOffItself()
    {
        var diagnostics = await RunAsync("""
            #pragma warning disable SEEDSUPPRESS002
            #pragma warning disable CS0168

            public sealed class Orders
            {
                public void Count()
                {
                    int unused;
                }
            }
            """);

        Assert.Equal(2, diagnostics.Effective().Count);
    }

    [Fact]
    public async Task GeneratedCode_IsIgnored()
    {
        var diagnostics = await RunAsync("""
            // <auto-generated/>
            #pragma warning disable

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822")]
            public sealed class Generated;
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Rules_IgnoreNoWarnAndEditorConfig()
    {
        // NoWarn und dotnet_diagnostic.<ID>.severity landen beide in diesen Optionen.
        var diagnostics = await AnalyzerRunner.RunAsync(
            new SeedSuppressionAnalyzer(),
            ["""
            #pragma warning disable CS0168
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822")]
            public sealed class Orders;
            """],
            new Dictionary<string, ReportDiagnostic>
            {
                ["SEEDSUPPRESS001"] = ReportDiagnostic.Suppress,
                ["SEEDSUPPRESS002"] = ReportDiagnostic.Suppress,
            });

        Assert.Equal(["SEEDSUPPRESS002", "SEEDSUPPRESS001"], diagnostics.Effective().Select(d => d.Id));
    }

    [Fact]
    public void Rules_AreErrorsAndCannotBeConfiguredAway()
    {
        var analyzer = new SeedSuppressionAnalyzer();

        Assert.All(analyzer.SupportedDiagnostics, d =>
        {
            Assert.Equal(DiagnosticSeverity.Error, d.DefaultSeverity);
            Assert.True(d.IsEnabledByDefault);
            Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, d.CustomTags);
            Assert.StartsWith("https://github.com/blackforestsentinel/seed-packages/", d.HelpLinkUri, StringComparison.Ordinal);
        });
        Assert.Equal(["SEEDSUPPRESS001", "SEEDSUPPRESS002"], analyzer.SupportedDiagnostics.Select(d => d.Id));
    }

    private static Task<IReadOnlyList<Diagnostic>> RunAsync(string source) =>
        AnalyzerRunner.RunAsync(new SeedSuppressionAnalyzer(), source);
}
