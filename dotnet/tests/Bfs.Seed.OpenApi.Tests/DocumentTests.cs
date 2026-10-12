namespace Bfs.Seed.OpenApi.Tests;

/// <summary>Dokument der Test-App: Pfade, Methoden, Parameter, Antworten, Anmeldung.</summary>
public class DocumentTests
{
    private static readonly SeedOpenApiResult App = TestDocuments.App;

    [Fact]
    public void Document_ContainsOnlyTheHttpFunctionsOfTheApp()
    {
        // Ohne Queue-Function, ohne [OpenApiIgnore] und ohne den MCP-Endpunkt aus dem referenzierten Bfs.Seed.Mcp.
        Assert.Equal(
            [
                "/archive/{year} get ListArchive",
                "/categories get ListCategories",
                "/health get GetHealth",
                "/me get GetMe",
                "/orders get ListOrders",
                "/orders post CreateOrder",
                "/orders/count get CountOrders",
                "/orders/{id} get GetOrder",
                "/orders/{id} put OrderPut",
                "/orders/{id} delete OrderDelete",
            ],
            App.Operations().Select(o => $"{o.Path} {o.Method} {o.Operation.Text("operationId")}"));
    }

    [Fact]
    public void Document_HasConnectorBasicsWithoutHost()
    {
        var document = App.Document;

        Assert.Equal("2.0", document.Text("swagger"));
        Assert.Null(document["host"]);
        Assert.Equal("/api", document.Text("basePath"));
        Assert.Equal(["https"], document["schemes"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Equal(["application/json"], document["consumes"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Equal(["application/json"], document["produces"]!.AsArray().Select(s => s!.GetValue<string>()));
    }

    [Fact]
    public void Info_ComesFromAssemblyAttributeAndInformationalVersion()
    {
        var info = App.Document["info"]!;

        Assert.Equal("Bestellungen", info.Text("title"));
        Assert.Equal("Beispiel-API für die Tests von Bfs.Seed.OpenApi.", info.Text("description"));
        Assert.Equal("1.2.3-test", info.Text("version"));
    }

    [Fact]
    public void Security_IsRequiredExceptForAllowAnonymousOnClassOrMethod()
    {
        var open = App.Operations().Where(o => o.Operation["security"] is null).Select(o => o.Operation.Text("operationId"));
        var oauth2 = App.Document["securityDefinitions"]!["oauth2"]!;

        Assert.Equal(["GetHealth", "CountOrders"], open);
        Assert.Equal("[{\"oauth2\":[]}]", App.Operation("/me", "get")["security"]!.ToJsonString());
        Assert.Equal("accessCode", oauth2.Text("flow"));
        Assert.Equal("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", oauth2.Text("authorizationUrl"));
        Assert.Equal("https://login.microsoftonline.com/common/oauth2/v2.0/token", oauth2.Text("tokenUrl"));
    }

    [Fact]
    public void PathParameter_TypeFromMethodParameterAndSingleUrlEncoding()
    {
        var id = App.Operation("/orders/{id}", "get").Parameter("id");

        Assert.Equal(
            """{"name":"id","in":"path","required":true,"description":"ID aus CreateOrder","x-ms-summary":"Bestell-ID","type":"string","format":"uuid","x-ms-url-encoding":"single"}""",
            id.ToJsonString());
    }

    [Fact]
    public void QueryParameters_ComeFromAttributes()
    {
        var operation = App.Operation("/orders", "get");

        Assert.Equal(
            """{"name":"top","in":"query","required":false,"description":"Höchstens so viele","x-ms-summary":"Anzahl","type":"integer","format":"int32"}""",
            operation.Parameter("top").ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Equal(
            """{"name":"state","in":"query","required":false,"x-ms-visibility":"advanced","type":"string","enum":["Open","Shipped","Cancelled"]}""",
            operation.Parameter("state").ToJsonString());
        Assert.Equal("""{"name":"customer","in":"query","required":true,"type":"string"}""", operation.Parameter("customer").ToJsonString());
    }

    [Fact]
    public void RequestBody_IsReferenceToDefinition()
    {
        var body = App.Operation("/orders", "post").Parameter("body");

        Assert.Equal("""{"name":"body","in":"body","required":true,"description":"Neue Bestellung","schema":{"$ref":"#/definitions/CreateOrderRequest"}}""", body.ToJsonString());
    }

    [Fact]
    public void SeveralMethods_GetOneOperationEachWithSuffix()
    {
        var item = App.Document["paths"]!["/orders/{id}"]!.AsObject();

        Assert.Equal(["get", "put", "delete"], item.Select(m => m.Key));
        Assert.Equal("Bestellung ersetzen oder löschen", item["delete"].Text("summary"));
        Assert.Equal("""{"204":{"description":"No Content"}}""", item["put"]!["responses"]!.ToJsonString());
    }

    [Fact]
    public void Responses_WithSchemaOrOnlyDescription()
    {
        var get = App.Operation("/orders/{id}", "get")["responses"]!;
        var list = App.Operation("/orders", "get")["responses"]!["200"]!;
        var count = App.Operation("/orders/count", "get")["responses"]!["200"]!;

        Assert.Equal("""{"200":{"description":"Bestellung","schema":{"$ref":"#/definitions/Order"}},"404":{"description":"Not Found"}}""", get.ToJsonString());
        Assert.Equal("""{"type":"array","items":{"$ref":"#/definitions/Order"}}""", list["schema"]!.ToJsonString());
        Assert.Equal("""{"type":"integer","format":"int32"}""", count["schema"]!.ToJsonString());
    }

    [Fact]
    public void Operations_CarryDescriptionAndVisibility()
    {
        Assert.Equal("Neueste zuerst.", App.Operation("/orders", "get").Text("description"));
        Assert.Equal("internal", App.Operation("/health", "get").Text("x-ms-visibility"));
        Assert.Equal("important", App.Operation("/orders", "post").Text("x-ms-visibility"));
        Assert.Null(App.Operation("/orders/{id}", "get")["x-ms-visibility"]);
    }

    [Fact]
    public void Warnings_OnlyForTheOptionalRouteParameter()
    {
        var warning = Assert.Single(App.Warnings);

        Assert.Equal("SEEDOPENAPI006", warning.Code);
        Assert.StartsWith("ListArchive: optionaler Routenparameter year", warning.Message, StringComparison.Ordinal);
        Assert.Equal("""{"name":"year","in":"path","required":true,"type":"integer","format":"int32","x-ms-url-encoding":"single"}""", App.Operation("/archive/{year}", "get").Parameter("year").ToJsonString());
    }

    [Fact]
    public void Document_IsTheSameOnEveryRun()
    {
        var again = SeedOpenApiDocument.Generate(typeof(TestApp.OrderFunctions).Assembly);

        Assert.Equal(App.ToJson(), again.ToJson());
        Assert.EndsWith("}\n", again.ToJson(), StringComparison.Ordinal);
        Assert.Contains("Höchstens", again.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Options_RoutePrefixAndWithoutOAuth()
    {
        var result = SeedOpenApiDocument.Generate(typeof(TestApp.OrderFunctions).Assembly, new SeedOpenApiOptions { RoutePrefix = "", OAuth2 = false });

        Assert.Equal("/", result.Document.Text("basePath"));
        Assert.Null(result.Document["securityDefinitions"]);
        Assert.All(result.Operations(), o => Assert.Null(o.Operation["security"]));
    }

    [Fact]
    public void Document_IsValidSwagger()
    {
        Assert.Empty(SwaggerValidator.Validate(App.Document));
        Assert.True(App.ToJson().Length < 1024 * 1024);
    }
}
