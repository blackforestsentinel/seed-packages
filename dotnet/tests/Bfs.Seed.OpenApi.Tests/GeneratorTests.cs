namespace Bfs.Seed.OpenApi.Tests;

/// <summary>Sonderfälle aus EdgeCaseFunctions.cs: Warnungen, Fehler, Routen.</summary>
public class GeneratorTests
{
    [Fact]
    public void RouteConstraints_GiveTypesAndSeveralMethodsGetSuffixes()
    {
        var result = TestDocuments.For(typeof(ConstraintFunctions));

        var item = result.Document["paths"]!["/items/{id}/{flag}/{day}/{name}"]!.AsObject();
        Assert.Equal(["get", "put"], item.Select(m => m.Key));
        Assert.Equal("ItemGet", item["get"].Text("operationId"));
        Assert.Equal("ItemPut", item["put"].Text("operationId"));

        var parameters = item["get"]!["parameters"]!.AsArray().Select(p => $"{p.Text("in")}:{p.Text("name")}:{p.Text("type")}:{p.Text("format")}");
        Assert.Equal(["path:id:integer:int32", "path:flag:boolean:", "path:day:string:date-time", "path:name:string:", "query:top:integer:int64"], parameters);
        Assert.Equal("""{"type":"string"}""", item["get"]!["responses"]!["200"]!["schema"]!.ToJsonString());
    }

    [Fact]
    public void OptionalAndCatchAllRouteParameters_GiveWarnings()
    {
        // Lücke (c): Swagger 2.0 kennt keine optionalen Pfadparameter; sie bleiben Pflicht, der Build warnt.
        var result = TestDocuments.For(typeof(RouteParameterFunctions));

        Assert.Equal(
            [
                "CatchAll: Catch-all-Parameter path",
                "Default: optionaler Routenparameter page ist in Swagger 2.0 Pflicht",
                "Optional: optionaler Routenparameter name ist in Swagger 2.0 Pflicht",
            ],
            result.Warnings.Where(w => w.Code == "SEEDOPENAPI006").Select(w => w.Message[..w.Message.IndexOf(';', StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
        Assert.True(result.Operation("/optional/{name}", "get").Parameter("name")["required"]!.GetValue<bool>());
        Assert.True(result.Document["paths"]!.AsObject().ContainsKey("/files/{path}"));
        Assert.True(result.Document["paths"]!.AsObject().ContainsKey("/default/{page}"));
    }

    [Fact]
    public void MissingAnnotations_FallBackToFunctionNameWithWarnings()
    {
        var result = TestDocuments.For(typeof(UnannotatedFunctions));

        var plain = result.Operation("/Plain", "get");
        Assert.Equal("Plain", plain.Text("operationId"));
        Assert.Equal("Plain", plain.Text("summary"));
        Assert.Equal("""{"200":{"description":"OK"}}""", plain["responses"]!.ToJsonString());
        Assert.Contains(new SeedOpenApiWarning("SEEDOPENAPI001", "Plain: ohne [OpenApiOperation]; operationId und summary sind der Function-Name"), result.Warnings);
        Assert.Contains(result.Warnings, w => w is { Code: "SEEDOPENAPI002" } && w.Message.StartsWith("Plain:", StringComparison.Ordinal));
    }

    [Fact]
    public void Methods_WithoutOrUnknownGiveWarnings()
    {
        var result = TestDocuments.For(typeof(UnannotatedFunctions));

        Assert.Equal(["get", "post"], result.Document["paths"]!["/any"]!.AsObject().Select(m => m.Key));
        Assert.Equal("AnyMethodPost", result.Operation("/any", "post").Text("operationId"));
        Assert.Equal(["get"], result.Document["paths"]!["/trace"]!.AsObject().Select(m => m.Key));
        Assert.Equal(2, result.Warnings.Count(w => w.Code == "SEEDOPENAPI005"));
    }

    [Fact]
    public void QuestionableFunction_GivesWarningsButValidDocument()
    {
        var result = TestDocuments.For(typeof(QuestionableFunctions));
        var operation = result.Operation("/questionable", "get");

        Assert.Equal(
            ["SEEDOPENAPI003", "SEEDOPENAPI004", "SEEDOPENAPI007", "SEEDOPENAPI008", "SEEDOPENAPI009", "SEEDOPENAPI012"],
            result.Codes().Distinct().Order(StringComparer.Ordinal));

        // Objekt als Query-Parameter: string. Polymorphie (anyOf): erster Zweig.
        Assert.Equal("""{"name":"filter","in":"query","required":false,"type":"string"}""", operation.Parameter("filter").ToJsonString());
        Assert.DoesNotContain("anyOf", result.ToJson(), StringComparison.Ordinal);
        Assert.Equal("object", result.Definition("Shape").Text("type"));
        Assert.Empty(SwaggerValidator.Validate(result.Document));
    }

    [Fact]
    public void SameDefinitionName_GetsNumberAndWarning()
    {
        var result = TestDocuments.For(typeof(CollidingNameFunctions));

        Assert.Equal("""{"$ref":"#/definitions/Item"}""", result.Operation("/colliding", "get")["responses"]!["200"]!["schema"]!.ToJsonString());
        Assert.Equal("""{"$ref":"#/definitions/Item2"}""", result.Operation("/colliding", "get")["responses"]!["201"]!["schema"]!.ToJsonString());
        Assert.Equal("integer", result.Properties("Item2")["number"].Text("type"));
        Assert.Contains(result.Warnings, w => w.Code == "SEEDOPENAPI010" && w.Message.Contains("Second+Item heißt Item2", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateOperationId_StopsTheBuild()
    {
        var error = Assert.Throws<SeedOpenApiException>(() => TestDocuments.For(typeof(DuplicateIdFunctions)));

        Assert.Equal("SEEDOPENAPI100", error.Code);
        Assert.Equal("operationId same gibt es mehrfach: Functions One und Two", error.Message);
    }

    [Fact]
    public void DuplicateMethodAndPath_StopsTheBuild()
    {
        var error = Assert.Throws<SeedOpenApiException>(() => TestDocuments.For(typeof(DuplicateRouteFunctions)));

        Assert.Equal("SEEDOPENAPI101", error.Code);
        Assert.Equal("GET /same/{id} gibt es mehrfach, zuletzt in Function Second", error.Message);
    }

    [Fact]
    public void WholeAssemblyWithDuplicates_Throws()
    {
        Assert.Throws<SeedOpenApiException>(() => SeedOpenApiDocument.Generate(typeof(GeneratorTests).Assembly));
    }

    [Fact]
    public void EdgeCaseDocuments_AreValidSwagger()
    {
        var result = TestDocuments.For(typeof(ConstraintFunctions), typeof(RouteParameterFunctions), typeof(UnannotatedFunctions), typeof(QuestionableFunctions), typeof(CollidingNameFunctions));

        Assert.Empty(SwaggerValidator.Validate(result.Document));
    }
}
