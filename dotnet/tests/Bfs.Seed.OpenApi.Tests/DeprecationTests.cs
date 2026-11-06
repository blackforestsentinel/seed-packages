namespace Bfs.Seed.OpenApi.Tests;

/// <summary>
/// Veraltete Operationen: <c>deprecated: true</c>, Zusatz <c>(deprecated)</c> an summary und description
/// wie von Microsoft empfohlen, Hinweis vorn in der description. Test-App und Sonderfälle aus EdgeCaseFunctions.cs.
/// </summary>
public class DeprecationTests
{
    private static readonly SeedOpenApiResult App = TestDocuments.App;

    [Fact]
    public void Obsolete_MarksOperationAndPutsMessageIntoDescription()
    {
        var operation = App.Operation("/shipments", "get");

        Assert.Equal(
            """{"summary":"Sendungen auflisten (deprecated)","description":"(deprecated) Stattdessen ListDeliveries verwenden. Alle Sendungen.","operationId":"ListShipments","deprecated":true}""",
            Head(operation));
    }

    [Fact]
    public void DeprecatedProperty_MarksOperationWithoutMessage()
    {
        var operation = App.Operation("/shipments/{id}", "get");

        Assert.Equal(
            """{"summary":"Sendung abrufen (deprecated)","description":"(deprecated)","operationId":"GetShipment","deprecated":true}""",
            Head(operation));
    }

    [Fact]
    public void OtherOperations_AreNotDeprecated()
    {
        var deprecated = App.Operations().Where(o => o.Operation["deprecated"] is not null).Select(o => o.Operation.Text("operationId"));

        Assert.Equal(["ListShipments", "GetShipment"], deprecated);
        Assert.DoesNotContain("(deprecated)", App.Operation("/orders", "get").ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeprecationMessage_AloneMarksAndGoesBeforeObsolete()
    {
        var operation = TestDocuments.For(typeof(DeprecationFunctions)).Operation("/message-first", "post");

        Assert.True(operation["deprecated"]!.GetValue<bool>());
        Assert.Equal("(deprecated) Stattdessen CreateOrder verwenden.", operation.Text("description"));
    }

    [Fact]
    public void ExistingMarker_IsNotRepeated()
    {
        var operation = TestDocuments.For(typeof(DeprecationFunctions)).Operation("/already-marked", "get");

        Assert.Equal("Schon markiert (Deprecated)", operation.Text("summary"));
        Assert.Equal("Alt (deprecated).", operation.Text("description"));
    }

    [Fact]
    public void SummaryNearLimit_KeepsTitleAndMarksOnlyDescription()
    {
        // Mit dem Zusatz wären es 83 Zeichen; der Connector lehnt über 80 ab.
        var result = TestDocuments.For(typeof(DeprecationFunctions));
        var operation = result.Operation("/long-summary", "get");

        Assert.Equal("Eine recht lange Zusammenfassung, die fast an die Grenze heranreicht", operation.Text("summary"));
        Assert.Equal("(deprecated)", operation.Text("description"));
        Assert.True(operation["deprecated"]!.GetValue<bool>());
        Assert.DoesNotContain(result.Warnings, w => w.Message.StartsWith("LongSummary:", StringComparison.Ordinal));
        Assert.Empty(SwaggerValidator.Validate(result.Document));
    }

    [Fact]
    public void DeprecatedFalse_WinsOverMessageAndObsolete()
    {
        var operation = TestDocuments.For(typeof(DeprecationFunctions)).Operation("/not-deprecated", "get");

        Assert.Null(operation["deprecated"]);
        Assert.Null(operation["description"]);
        Assert.Equal("Nicht veraltet", operation.Text("summary"));
    }

    [Fact]
    public void DeprecatedProperty_FollowsMessageUnlessSetExplicitly()
    {
        Assert.False(new OpenApiOperationAttribute("A", "a").Deprecated);
        Assert.True(new OpenApiOperationAttribute("A", "a") { DeprecationMessage = "Stattdessen B." }.Deprecated);
        Assert.False(new OpenApiOperationAttribute("A", "a") { DeprecationMessage = "Stattdessen B.", Deprecated = false }.Deprecated);
        Assert.True(new OpenApiOperationAttribute("A", "a") { Deprecated = true }.Deprecated);
    }

    [Fact]
    public void ObsoleteOnClass_IsIgnored()
    {
        // Mit [Obsolete] an der Klasse bricht schon der Build der App (CS0618 im Code des Functions-SDK).
#pragma warning disable CS0618
        var result = TestDocuments.For(typeof(ObsoleteClassFunctions));
#pragma warning restore CS0618

        Assert.Null(result.Operation("/obsolete-class", "get")["deprecated"]);
    }

    /// <summary>summary, description, operationId und deprecated, ohne Parameter und Antworten.</summary>
    private static string Head(System.Text.Json.Nodes.JsonObject operation) =>
        new System.Text.Json.Nodes.JsonObject([.. operation.Where(p => p.Key is "summary" or "description" or "operationId" or "deprecated").Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))])
            .ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
