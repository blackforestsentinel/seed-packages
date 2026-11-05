using System.Text.Json.Nodes;

namespace Bfs.Seed.OpenApi.Tests;

/// <summary>
/// Enums: Pfad und Query immer als string mit den Namen im Code; im Body ohne Converter Zahlen mit
/// Anzeigetexten (x-ms-enum-values), mit JsonStringEnumConverter wie System.Text.Json. Ohne Werteliste
/// warnt der Generator (SEEDOPENAPI014).
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

        // Liste mit Converter und JsonStringEnumMemberName: Namen im Code, wie Enum.TryParse sie annimmt.
        Assert.Equal(
            """{"name":"states","in":"query","required":false,"type":"string","enum":["Planned","InTransit","Delivered"]}""",
            operation.Parameter("states").ToJsonString());
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

        // Query: auch [Flags] mit Converter und Listen nullbarer Enums mit eigenem Converter bekommen die Namen.
        Assert.Equal("""{"name":"nothing","in":"query","required":false,"type":"string"}""", operation.Parameter("nothing").ToJsonString());
        Assert.Equal("""["None","Read","Write"]""", operation.Parameter("rights")["enum"]!.ToJsonString());
        Assert.Equal("""["Alpha","Beta"]""", operation.Parameter("codes")["enum"]!.ToJsonString());

        // Body: [Flags] ohne Converter mit den einzelnen Werten, gleiche Zahl nur einmal, ulong ohne Überlauf.
        Assert.Equal("""[0,1,2]""", request["rights"]!["enum"]!.ToJsonString());
        Assert.Equal("""{"type":"integer","enum":[1,2],"x-ms-enum-values":[{"value":1,"displayName":"First"},{"value":2,"displayName":"Second"}]}""", request["alias"]!.ToJsonString());
        Assert.Equal("""[18446744073709551615]""", request["huge"]!["enum"]!.ToJsonString());
        Assert.Equal("""{"type":"integer","enum":[1,2],"x-ms-enum-values":[{"value":1,"displayName":"First"},{"value":2,"displayName":"Second"}]}""", operation["responses"]!["200"]!["schema"]!.ToJsonString());

        Assert.Empty(SwaggerValidator.Validate(result.Document));
    }

    [Fact]
    public void EnumWithoutValues_GivesWarning()
    {
        var result = TestDocuments.For(typeof(EnumEdgeCaseFunctions));

        Assert.Equal(
            [
                "EnumEdgeCaseRequest.code: Enum Code ohne Werteliste, System.Text.Json nennt keine Werte, etwa bei einem eigenen JsonConverter; der Connector zeigt keine Auswahl",
                "EnumEdgeCaseRequest.nothing: Enum EmptyEnum ohne Werteliste, es hat keine Werte; der Connector zeigt keine Auswahl",
                "EnumEdgeCaseRequest.stringRights: Enum StringRights ohne Werteliste, System.Text.Json nennt keine Werte, etwa bei [Flags] mit JsonStringEnumConverter; der Connector zeigt keine Auswahl",
                "EnumEdgeCases.nothing: Enum EmptyEnum ohne Werteliste, es hat keine Werte; der Connector zeigt keine Auswahl",
            ],
            result.Warnings.Where(w => w.Code == "SEEDOPENAPI014").Select(w => w.Message).Order(StringComparer.Ordinal));
        Assert.Equal("""{"type":"integer"}""", result.Properties("EnumEdgeCaseRequest")["nothing"]!.ToJsonString());
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
