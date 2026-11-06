using System.Text.Json.Nodes;

namespace Bfs.Seed.OpenApi.Tests;

/// <summary>
/// Enums: Pfad und Query immer als string mit den Namen im Code; im Body ohne Converter Zahlen mit
/// Anzeigetexten (x-ms-enum-values), mit JsonStringEnumConverter wie System.Text.Json. [Flags] und
/// Listen in der Query wie in 0.9.0, ohne Werteliste. Ohne Werteliste warnt der Generator nur für
/// Parameter und Anfrage-Body (SEEDOPENAPI014).
/// </summary>
public class EnumTests
{
    private const string PriorityValues = """
        "enum":[0,5,10],"x-ms-enum-values":[{"value":0,"displayName":"Low"},{"value":5,"displayName":"Normal"},{"value":10,"displayName":"High"}]
        """;

    private static readonly SeedOpenApiResult App = TestDocuments.App;

    [Fact]
    public void QueryParameters_AreStringsWithNamesWhateverTheConverter()
    {
        var operation = App.Operation("/deliveries", "get");

        // Fremdes Enum ohne Converter, nullbares Enum ohne Converter.
        Assert.Equal(
            """{"name":"day","in":"query","required":false,"type":"string","enum":["Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"]}""",
            operation.Parameter("day").ToJsonString());
        Assert.Equal(
            """{"name":"priority","in":"query","required":false,"type":"string","enum":["Low","Normal","High"]}""",
            operation.Parameter("priority").ToJsonString());

        // Mit Converter und JsonStringEnumMemberName: Namen im Code, wie Enum.TryParse sie annimmt.
        Assert.Equal(
            """{"name":"state","in":"query","required":false,"type":"string","enum":["Planned","InTransit","Delivered"]}""",
            operation.Parameter("state").ToJsonString());
    }

    [Fact]
    public void PathParameter_IsStringWithNames()
    {
        var priority = App.Operation("/deliveries/priority/{priority}", "get").Parameter("priority");

        Assert.Equal(
            """{"name":"priority","in":"path","required":true,"type":"string","enum":["Low","Normal","High"],"x-ms-url-encoding":"single"}""",
            priority.ToJsonString());
    }

    [Fact]
    public void BodyWithoutConverter_HasNumbersAndDisplayNames()
    {
        var delivery = App.Properties("Delivery");

        Assert.Equal(Priority(""), delivery["priority"]!.ToJsonString());
        Assert.Equal(Priority(",\"x-nullable\":true"), delivery["fallback"]!.ToJsonString());
        Assert.Equal("{\"type\":\"array\",\"items\":" + Priority("") + "}", delivery["history"]!.ToJsonString());
        Assert.Equal("Sunday", delivery["day"]!["x-ms-enum-values"]![0].Text("displayName"));
        Assert.Equal(Priority(",\"description\":\"Dringlichkeit.\""), App.Properties("CreateDeliveryRequest")["priority"]!.ToJsonString());
    }

    [Fact]
    public void BodyWithConverter_KeepsJsonNames()
    {
        Assert.Equal("""{"type":"string","enum":["Planned","in-transit","Delivered"]}""", App.Properties("Delivery")["state"]!.ToJsonString());
        Assert.Equal("""{"type":"string","enum":["Open","Shipped","Cancelled"]}""", App.Properties("Order")["state"]!.ToJsonString());
    }

    [Fact]
    public void EdgeCases_GetValuesWherePossible()
    {
        var result = TestDocuments.For(typeof(EnumEdgeCaseFunctions));
        var operation = result.Operation("/enum-edge-cases", "post");
        var request = result.Properties("EnumEdgeCaseRequest");

        Assert.Equal("""{"name":"nothing","in":"query","required":false,"type":"string"}""", operation.Parameter("nothing").ToJsonString());

        // Body: gleiche Zahl nur einmal, ulong ohne Überlauf.
        Assert.Equal("""{"type":"integer","enum":[1,2],"x-ms-enum-values":[{"value":1,"displayName":"First"},{"value":2,"displayName":"Second"}]}""", request["alias"]!.ToJsonString());
        Assert.Equal("""[18446744073709551615]""", request["huge"]!["enum"]!.ToJsonString());
        Assert.Equal("""{"type":"integer","enum":[1,2],"x-ms-enum-values":[{"value":1,"displayName":"First"},{"value":2,"displayName":"Second"}]}""", operation["responses"]!["200"]!["schema"]!.ToJsonString());

        Assert.Empty(SwaggerValidator.Validate(result.Document));
    }

    [Fact]
    public void Flags_HaveNoValueListAndNoWarning()
    {
        // Kombinationen wie Read | Write = 3 sind gültig; eine Liste der Einzelwerte schlösse sie aus.
        var result = TestDocuments.For(typeof(EnumEdgeCaseFunctions));
        var operation = result.Operation("/enum-edge-cases", "post");
        var request = result.Properties("EnumEdgeCaseRequest");

        Assert.Equal("""{"name":"rights","in":"query","required":false,"type":"string"}""", operation.Parameter("rights").ToJsonString());
        Assert.Equal("""{"name":"plainRights","in":"query","required":false,"type":"integer"}""", operation.Parameter("plainRights").ToJsonString());
        Assert.Equal("""{"type":"integer"}""", request["rights"]!.ToJsonString());
        Assert.Equal("""{"type":"string"}""", request["stringRights"]!.ToJsonString());
        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("Rights", StringComparison.Ordinal));
    }

    [Fact]
    public void QueryLists_StayStringsAsIn090()
    {
        // Mehrere Werte passen nicht in eine Auswahl mit einem Wert; Flows, die heute Text setzen, laufen weiter.
        var result = TestDocuments.For(typeof(EnumEdgeCaseFunctions));
        var operation = result.Operation("/enum-edge-cases", "post");

        Assert.Equal("""{"name":"codes","in":"query","required":false,"type":"string"}""", operation.Parameter("codes").ToJsonString());
        Assert.Equal("""{"name":"aliases","in":"query","required":false,"type":"string"}""", operation.Parameter("aliases").ToJsonString());
        Assert.Equal(
            ["EnumEdgeCases.aliases: Typ IReadOnlyList`1 ist als Parameter nicht abbildbar, verwende string", "EnumEdgeCases.codes: Typ Nullable`1[] ist als Parameter nicht abbildbar, verwende string"],
            result.Warnings.Where(w => w.Code == "SEEDOPENAPI008").Select(w => w.Message).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EnumWithoutValues_WarnsOnlyForParametersAndRequestBody()
    {
        var result = TestDocuments.For(typeof(EnumEdgeCaseFunctions));

        // EnumHolder entsteht zuerst als Antwort und warnt, weil ihn auch ein Body erreicht; EnumResponse nicht.
        Assert.Equal(
            [
                "Enum Code ohne Werteliste, System.Text.Json nennt keine Werte, etwa bei einem eigenen JsonConverter; der Connector zeigt keine Auswahl",
                "EnumEdgeCaseRequest.code: Enum Code ohne Werteliste, System.Text.Json nennt keine Werte, etwa bei einem eigenen JsonConverter; der Connector zeigt keine Auswahl",
                "EnumEdgeCaseRequest.nothing: Enum EmptyEnum ohne Werteliste, es hat keine Werte; der Connector zeigt keine Auswahl",
                "EnumEdgeCases.nothing: Enum EmptyEnum ohne Werteliste, es hat keine Werte; der Connector zeigt keine Auswahl",
                "EnumHolder.held: Enum Code ohne Werteliste, System.Text.Json nennt keine Werte, etwa bei einem eigenen JsonConverter; der Connector zeigt keine Auswahl",
            ],
            result.Warnings.Where(w => w.Code == "SEEDOPENAPI014").Select(w => w.Message).Order(StringComparer.Ordinal));
        Assert.Equal("""{"type":"integer"}""", result.Properties("EnumEdgeCaseRequest")["nothing"]!.ToJsonString());
        Assert.Equal("""{"type":"integer"}""", result.Properties("EnumResponse")["nothing"]!.ToJsonString());

        // Liste mit eigenem Converter: Elemente offen, aber mit items, wie Swagger 2.0 es verlangt.
        Assert.Equal("""{"type":"array","items":{}}""", result.Operation("/enum-inline", "post").Parameter("body")["schema"]!.ToJsonString());
        Assert.Equal("""{"type":"array","items":{}}""", result.Operation("/enum-answer", "get")["responses"]!["202"]!["schema"]!.ToJsonString());
    }

    [Fact]
    public void EnumWithoutValuesOnlyInResponses_GivesNoWarning()
    {
        var result = TestDocuments.For(typeof(EnumEdgeCaseFunctions));
        var answer = TestDocuments.For(typeof(EnumAnswerOnlyFunctions));

        Assert.DoesNotContain(result.Warnings, w => w.Message.Contains("EnumResponse", StringComparison.Ordinal));
        Assert.DoesNotContain("SEEDOPENAPI014", answer.Codes());
    }

    [Fact]
    public void TestApp_HasNoEnumWithoutValues()
    {
        Assert.DoesNotContain("SEEDOPENAPI014", App.Codes());
        Assert.All(AllEnums(App.Document), e => Assert.True(e.Count > 0));
    }

    /// <summary>Schema von Priority ohne Converter, mit weiteren Schlüsseln am Ende.</summary>
    private static string Priority(string more) => "{\"type\":\"integer\"," + PriorityValues + more + "}";

    /// <summary>Alle enum-Listen im Dokument, an Parametern und in Schemas.</summary>
    private static IEnumerable<JsonArray> AllEnums(JsonNode? node) => node switch
    {
        JsonObject o => (o["enum"] is JsonArray e ? [e] : Enumerable.Empty<JsonArray>()).Concat(o.SelectMany(p => AllEnums(p.Value))),
        JsonArray a => a.SelectMany(AllEnums),
        _ => [],
    };
}
