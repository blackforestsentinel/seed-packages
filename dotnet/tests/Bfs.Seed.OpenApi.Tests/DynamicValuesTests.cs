namespace Bfs.Seed.OpenApi.Tests;

/// <summary>x-ms-dynamic-values: Kunden-Auswahl der Test-App, Sonderfälle aus EdgeCaseFunctions.cs.</summary>
public class DynamicValuesTests
{
    private static readonly SeedOpenApiResult App = TestDocuments.App;

    [Fact]
    public void QueryParameter_GetsValuesFromInternalOperation()
    {
        var customer = App.Operation("/orders", "get").Parameter("customer");

        Assert.Equal(
            """{"operationId":"ListCustomers","value-path":"name","value-title":"name","value-collection":"items","parameters":{"active":"true"}}""",
            customer["x-ms-dynamic-values"]!.ToJsonString());
        Assert.Equal("internal", App.Operation("/customers", "get").Text("x-ms-visibility"));
    }

    [Fact]
    public void BodyProperty_GetsValuesInDefinition()
    {
        // parameters ist laut Microsoft Pflicht, auch leer.
        Assert.Equal(
            """{"type":"string","description":"Name des Kunden.","x-ms-dynamic-values":{"operationId":"ListCustomers","value-path":"name","value-title":"name","value-collection":"items","parameters":{}}}""",
            App.Properties("CreateOrderRequest")["customer"]!.ToJsonString());
    }

    [Fact]
    public void Parameters_FixedValuesAndReferences_SourceWithSeveralMethods()
    {
        var result = TestDocuments.For(typeof(DynamicValuesSourceFunctions), typeof(DynamicValuesFunctions));

        Assert.Equal(
            """{"name":"processId","in":"query","required":true,"type":"string","x-ms-dynamic-values":{"operationId":"ProcessesGet","value-path":"id","value-title":"title","parameters":{"projectId":{"parameter":"projectId"},"mode":"write"}}}""",
            result.Operation("/projects/{projectId}/tasks", "post").Parameter("processId").ToJsonString());
        Assert.Equal(
            """{"name":"processId","in":"path","required":true,"type":"string","x-ms-dynamic-values":{"operationId":"ListWritableProcesses","value-path":"id","value-title":"title","value-collection":"value","parameters":{}},"x-ms-url-encoding":"single"}""",
            result.Operation("/processes/{processId}", "get").Parameter("processId").ToJsonString());
        Assert.Equal(
            """{"type":"string","x-ms-dynamic-values":{"operationId":"ProcessesGet","value-path":"id","value-title":"title","parameters":{"projectId":{"parameter":"projectId"},"mode":"read"}},"x-nullable":true}""",
            result.Properties("TaskRequest")["followUpProcessId"]!.ToJsonString());
        Assert.DoesNotContain("SEEDOPENAPI013", result.Codes());
        Assert.Empty(SwaggerValidator.Validate(result.Document));
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("=write")]
    [InlineData("projectId={}")]
    [InlineData("projectId={ }")]
    [InlineData("mode=read;mode=write")]
    public void Parameters_OnlyNameEqualsValueOncePerName(string entries)
    {
        var attribute = new OpenApiDynamicValuesAttribute("processId", "ListWritableProcesses") { Parameters = entries.Split(';') };

        var error = Assert.Throws<SeedOpenApiException>(() => DynamicValues.Extension(attribute, "Test.processId"));

        Assert.Equal("SEEDOPENAPI103", error.Code);
    }

    [Fact]
    public void UnknownOperationId_StopsTheBuildWithCandidates()
    {
        var error = Assert.Throws<SeedOpenApiException>(() => TestDocuments.For(typeof(DynamicValuesSourceFunctions), typeof(DynamicValuesUnknownFunctions)));

        Assert.Equal("SEEDOPENAPI102", error.Code);
        Assert.Equal("Pick.processId: x-ms-dynamic-values nennt operationId Processes, die es im Dokument nicht gibt; gemeint ist wohl ProcessesGet oder ProcessesPost", error.Message);
    }

    [Fact]
    public void UnknownOperationIdInBody_StopsTheBuild()
    {
        var error = Assert.Throws<SeedOpenApiException>(() => TestDocuments.For(typeof(DynamicValuesUnknownInBodyFunctions)));

        Assert.Equal("SEEDOPENAPI102", error.Code);
        Assert.Equal("AssignRequest.processId: x-ms-dynamic-values nennt operationId ListWritableProcesses, die es im Dokument nicht gibt", error.Message);
    }

    [Fact]
    public void Mismatches_GiveWarnings()
    {
        var result = TestDocuments.For(typeof(DynamicValuesSourceFunctions), typeof(DynamicValuesMismatchFunctions));

        Assert.Equal(
            [
                "Mismatch.a: Pflichtparameter mode von ProcessesGet fehlt in Parameters",
                "Mismatch.a: ProcessesGet hat keinen Parameter limit",
                "Mismatch.a: Verweis auf Parameter project, den Mismatch nicht hat",
                "Mismatch.a: value-collection items fehlt in der Antwort von ProcessesGet",
                "Mismatch.b: value-path key fehlt in den Elementen der Antwort von ProcessesGet",
                "Mismatch.b: value-title title/text fehlt in den Elementen der Antwort von ProcessesGet",
                "Mismatch.c: Antwort von ListWritableProcesses ist keine Liste; value-collection fehlt",
                "Mismatch.d: value-path fehlt, die Elemente der Antwort von ListWritableProcesses sind Objekte",
            ],
            result.Warnings.Where(w => w.Code == "SEEDOPENAPI013").Select(w => w.Message).Order(StringComparer.Ordinal));
        Assert.Empty(SwaggerValidator.Validate(result.Document));
    }

    [Theory]
    [InlineData(typeof(DynamicValuesWrongParameterFunctions), "Wrong: [OpenApiDynamicValues] für Parameter process, den es als Pfad- oder Query-Parameter nicht gibt; für ein Feld im Body gehört das Attribut an die Eigenschaft")]
    [InlineData(typeof(DynamicValuesWithoutParameterFunctions), "Nameless: [OpenApiDynamicValues] an der Function braucht den Namen des Parameters, etwa [OpenApiDynamicValues(\"id\", \"ListWritableProcesses\")]")]
    [InlineData(typeof(DynamicValuesMalformedFunctions), "Malformed.processId: Parameters von [OpenApiDynamicValues] enthält \"mode\"; erwartet ist je Parameter einmal name=wert oder name={parameter}")]
    [InlineData(typeof(DynamicValuesNamedPropertyFunctions), "NamedRequest.processId: an einer Eigenschaft gibt es [OpenApiDynamicValues] nur einmal und ohne Parameternamen, etwa [property: OpenApiDynamicValues(\"ListWritableProcesses\")]")]
    public void WrongAttribute_StopsTheBuild(Type functions, string message)
    {
        var error = Assert.Throws<SeedOpenApiException>(() => TestDocuments.For(typeof(DynamicValuesSourceFunctions), functions));

        Assert.Equal("SEEDOPENAPI103", error.Code);
        Assert.Equal(message, error.Message);
    }
}
