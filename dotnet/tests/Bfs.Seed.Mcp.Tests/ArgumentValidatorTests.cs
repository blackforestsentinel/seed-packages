using System.Text.Json;

namespace Bfs.Seed.Mcp.Tests;

public class ArgumentValidatorTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "text": { "type": "string", "minLength": 2, "maxLength": 5, "pattern": "^[a-z]+$" },
            "anzahl": { "type": "integer", "minimum": 1, "maximum": 10 },
            "art": { "type": "string", "enum": ["a", "b"] },
            "optional": { "type": ["string", "null"] },
            "liste": { "type": "array", "items": { "type": "integer" }, "maxItems": 2 },
            "filter": {
              "type": "object",
              "properties": { "von": { "type": "string" } },
              "required": ["von"],
              "additionalProperties": false
            }
          },
          "required": ["text"]
        }
        """).RootElement;

    [Fact]
    public void ValidArguments_HaveNoErrors()
    {
        Assert.Empty(Validate("""{"text":"abc","anzahl":3,"art":"b","optional":null,"liste":[1,2],"filter":{"von":"x"}}"""));
    }

    [Fact]
    public void IntegralNumber_CountsAsInteger()
    {
        Assert.Empty(Validate("""{"text":"abc","anzahl":3.0}"""));
    }

    [Theory]
    [InlineData("""{}""", "Pflichtargument „text“ fehlt.")]
    [InlineData("""{"text":"abc","txt":"x"}""", "Unbekanntes Argument „txt“")]
    [InlineData("""{"text":1}""", "„text“ muss vom Typ Text sein.")]
    [InlineData("""{"text":"a"}""", "„text“ muss mindestens 2 Zeichen lang sein.")]
    [InlineData("""{"text":"abcdef"}""", "„text“ darf höchstens 5 Zeichen lang sein.")]
    [InlineData("""{"text":"ABC"}""", "„text“ hat nicht das erwartete Format")]
    [InlineData("""{"text":"abc","anzahl":2.5}""", "„anzahl“ muss vom Typ ganze Zahl sein.")]
    [InlineData("""{"text":"abc","anzahl":0}""", "„anzahl“ muss mindestens 1 sein.")]
    [InlineData("""{"text":"abc","anzahl":11}""", "„anzahl“ darf höchstens 10 sein.")]
    [InlineData("""{"text":"abc","art":"c"}""", "„art“ muss einer dieser Werte sein: \"a\", \"b\".")]
    [InlineData("""{"text":"abc","optional":5}""", "„optional“ muss vom Typ Text oder null sein.")]
    [InlineData("""{"text":"abc","liste":[1,2,3]}""", "„liste“ darf höchstens 2 Einträge haben.")]
    [InlineData("""{"text":"abc","liste":["x"]}""", "„liste[0]“ muss vom Typ ganze Zahl sein.")]
    [InlineData("""{"text":"abc","filter":{}}""", "Pflichtargument „filter.von“ fehlt.")]
    [InlineData("""{"text":"abc","filter":{"von":"x","bis":"y"}}""", "Unbekanntes Argument „filter.bis“")]
    public void InvalidArguments_AreReported(string arguments, string expected)
    {
        Assert.Contains(Validate(arguments), e => e.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownArgument_NamesTheAllowedOnes()
    {
        var error = Assert.Single(Validate("""{"text":"abc","txt":"x"}"""));

        Assert.Equal("Unbekanntes Argument „txt“ (erlaubt: anzahl, art, filter, liste, optional, text).", error);
    }

    [Fact]
    public void ToolWithoutParameters_RejectsEveryArgument()
    {
        var empty = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

        var errors = SeedMcpArgumentValidator.Validate(empty, Arguments("""{"x":1}"""));

        Assert.Equal(["Unbekanntes Argument „x“ (erlaubt: keine)."], errors);
    }

    [Fact]
    public void MissingArguments_AreTreatedAsEmpty()
    {
        var empty = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

        Assert.Empty(SeedMcpArgumentValidator.Validate(empty, null));
    }

    private static IReadOnlyList<string> Validate(string arguments) => SeedMcpArgumentValidator.Validate(Schema, Arguments(arguments));

    private static Dictionary<string, JsonElement> Arguments(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}
