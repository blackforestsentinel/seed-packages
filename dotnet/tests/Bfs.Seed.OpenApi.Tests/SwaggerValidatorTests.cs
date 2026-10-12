using System.Text.Json.Nodes;

namespace Bfs.Seed.OpenApi.Tests;

/// <summary>Der Validator der Tests findet die Fehler, die der Generator vermeiden soll.</summary>
public class SwaggerValidatorTests
{
    [Fact]
    public void Validator_FindsTypeListsAnyOfAndBrokenReferences()
    {
        var document = JsonNode.Parse("""
            {
              "swagger": "2.0",
              "info": { "title": "Kaputt", "version": "1" },
              "basePath": "/api",
              "paths": {
                "/items/{id}": {
                  "get": {
                    "operationId": "GetItem",
                    "parameters": [{ "name": "q", "in": "query", "type": "object" }],
                    "responses": { "200": { "description": "OK", "schema": { "$ref": "#/definitions/Missing" } } }
                  }
                }
              },
              "definitions": {
                "Item": {
                  "type": "object",
                  "properties": { "name": { "type": ["string", "null"] }, "kind": { "anyOf": [{ "type": "string" }] } },
                  "required": ["name", "other"]
                }
              }
            }
            """)!.AsObject();

        var errors = SwaggerValidator.Validate(document);

        Assert.Contains(errors, e => e.Contains("q: type", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Pfadparameter fehlen", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("#/definitions/Missing ins Leere", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Item.name: type", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("anyOf gibt es in Swagger 2.0 nicht", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("required nennt unbekannte Eigenschaft", StringComparison.Ordinal));
    }
}
