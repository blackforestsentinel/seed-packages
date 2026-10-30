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

    [Fact]
    public void Validator_FindsBrokenDynamicValues()
    {
        var document = JsonNode.Parse("""
            {
              "swagger": "2.0",
              "info": { "title": "Kaputt", "version": "1" },
              "basePath": "/api",
              "paths": {
                "/tasks": {
                  "post": {
                    "operationId": "CreateTask",
                    "parameters": [
                      { "name": "processId", "in": "query", "type": "string", "x-ms-dynamic-values": { "operationId": "listprocesses", "valuePath": "id", "parameters": {} } },
                      { "name": "body", "in": "body", "schema": { "$ref": "#/definitions/Task" } }
                    ],
                    "responses": { "204": { "description": "No Content" } }
                  }
                },
                "/processes": {
                  "get": { "operationId": "ListProcesses", "responses": { "200": { "description": "OK" } } }
                }
              },
              "definitions": {
                "Task": {
                  "type": "object",
                  "properties": {
                    "processId": { "type": "string", "x-ms-dynamic-values": { "operationId": "ListProcesses", "value-path": "/id" } },
                    "stepId": { "type": "string", "x-ms-dynamic-values": { "operationId": "ListProcesses", "parameters": { "process": { "param": "processId" } } } }
                  }
                }
              }
            }
            """)!.AsObject();

        var errors = SwaggerValidator.Validate(document);

        Assert.Contains("post /tasks.processId: x-ms-dynamic-values mit unbekanntem Schlüssel", errors);
        Assert.Contains("post /tasks.processId: x-ms-dynamic-values nennt keine operationId des Dokuments", errors);
        Assert.Contains("definitions.Task.processId: value-path ist kein Pfad ohne führenden /", errors);
        Assert.Contains("definitions.Task.processId: x-ms-dynamic-values ohne parameters", errors);
        Assert.Contains(errors, e => e.StartsWith("definitions.Task.stepId: Parameter process ist weder", StringComparison.Ordinal));
    }
}
