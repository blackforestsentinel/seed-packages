using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bfs.Seed.OpenApi.Tests;

/// <summary>
/// Prüft die Regeln von Swagger 2.0, an denen der Custom Connector scheitern würde: Pflichtfelder,
/// Parameter, auflösbare Verweise, nur Schlüsselwörter aus Swagger 2.0 (keine Typ-Listen, kein anyOf).
/// Kein vollständiger Validator; im Build prüft zusätzlich swagger-cli (README).
/// </summary>
internal static partial class SwaggerValidator
{
    private static readonly HashSet<string> Methods = ["get", "put", "post", "delete", "options", "head", "patch"];
    private static readonly HashSet<string> Types = ["string", "number", "integer", "boolean", "array", "object", "file"];
    private static readonly HashSet<string> ParameterTypes = ["string", "number", "integer", "boolean", "array"];
    private static readonly HashSet<string> Locations = ["path", "query", "header", "body", "formData"];

    /// <summary>Schlüsselwörter des Schema-Objekts in Swagger 2.0; dazu kommen x-…</summary>
    private static readonly HashSet<string> SchemaKeywords =
    [
        "$ref", "format", "title", "description", "default", "multipleOf", "maximum", "exclusiveMaximum", "minimum",
        "exclusiveMinimum", "maxLength", "minLength", "pattern", "maxItems", "minItems", "uniqueItems", "maxProperties",
        "minProperties", "required", "enum", "type", "items", "allOf", "properties", "additionalProperties",
        "discriminator", "readOnly", "xml", "externalDocs", "example",
    ];

    public static IReadOnlyList<string> Validate(JsonObject document)
    {
        var errors = new List<string>();
        var definitions = document["definitions"] as JsonObject ?? [];
        var securityDefinitions = document["securityDefinitions"] as JsonObject ?? [];

        Check(document.Text("swagger") == "2.0", "swagger ist nicht 2.0");
        Check(!string.IsNullOrWhiteSpace(document["info"].Text("title")), "info.title fehlt");
        Check(!string.IsNullOrWhiteSpace(document["info"].Text("version")), "info.version fehlt");
        Check(document.Text("basePath")?.StartsWith('/') == true, "basePath beginnt nicht mit /");

        var operationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document["paths"] is not JsonObject paths)
        {
            errors.Add("paths fehlt");
            return errors;
        }

        foreach (var (path, item) in paths)
        {
            Check(path.StartsWith('/'), $"Pfad {path} beginnt nicht mit /");
            var templateNames = PathParameter().Matches(path).Select(m => m.Groups[1].Value).ToHashSet();
            foreach (var (method, node) in item!.AsObject())
            {
                var where = $"{method} {path}";
                Check(Methods.Contains(method), $"{where}: unbekannte Methode");
                var operation = node!.AsObject();
                Check(operation.Text("operationId") is { } id && operationIds.Add(id), $"{where}: operationId fehlt oder doppelt");

                var pathNames = new HashSet<string>();
                foreach (var parameter in operation["parameters"]?.AsArray().Select(p => p!.AsObject()) ?? [])
                {
                    var name = parameter.Text("name");
                    var location = parameter.Text("in");
                    Check(name is not null && location is not null && Locations.Contains(location), $"{where}: Parameter ohne name oder in");
                    Check(parameter["required"]?.GetValueKind() is null or JsonValueKind.True or JsonValueKind.False, $"{where}.{name}: required ist kein bool");
                    if (location == "body")
                    {
                        Check(parameter["schema"] is JsonObject && parameter["type"] is null, $"{where}.{name}: body braucht schema statt type");
                        Schema(parameter["schema"], $"{where}.{name}");
                    }
                    else
                    {
                        Check(ParameterTypes.Contains(parameter.Text("type") ?? ""), $"{where}.{name}: type {parameter["type"]?.ToJsonString()} ist für Parameter nicht erlaubt");
                    }

                    if (location == "path")
                    {
                        Check(parameter["required"]?.GetValue<bool>() == true, $"{where}.{name}: Pfadparameter muss required sein");
                        Check(templateNames.Contains(name!), $"{where}.{name}: steht nicht im Pfad");
                        pathNames.Add(name!);
                    }
                }

                Check(templateNames.SetEquals(pathNames), $"{where}: Pfadparameter fehlen");

                if (operation["responses"] is not JsonObject { Count: > 0 } responses)
                {
                    errors.Add($"{where}: responses fehlt");
                    continue;
                }

                foreach (var (status, response) in responses)
                {
                    Check(status == "default" || int.TryParse(status, out _), $"{where}: Status {status}");
                    Check(response.Text("description") is not null, $"{where} {status}: description fehlt");
                    if (response?["schema"] is { } schema)
                    {
                        Schema(schema, $"{where} {status}");
                    }
                }

                foreach (var requirement in operation["security"]?.AsArray() ?? [])
                {
                    foreach (var (name, _) in requirement!.AsObject())
                    {
                        Check(securityDefinitions.ContainsKey(name), $"{where}: securityDefinition {name} fehlt");
                    }
                }
            }
        }

        foreach (var (name, schema) in definitions)
        {
            Schema(schema, $"definitions.{name}");
        }

        return errors;

        void Check(bool condition, string message)
        {
            if (!condition)
            {
                errors.Add(message);
            }
        }

        void Schema(JsonNode? node, string where)
        {
            if (node is not JsonObject schema)
            {
                errors.Add($"{where}: Schema ist kein Objekt");
                return;
            }

            foreach (var (key, _) in schema)
            {
                Check(SchemaKeywords.Contains(key) || key.StartsWith("x-", StringComparison.Ordinal), $"{where}: {key} gibt es in Swagger 2.0 nicht");
            }

            if (schema["$ref"] is { } reference)
            {
                var target = reference.GetValueKind() == JsonValueKind.String ? reference.GetValue<string>() : "";
                Check(target.StartsWith("#/definitions/", StringComparison.Ordinal) && definitions.ContainsKey(target["#/definitions/".Length..]), $"{where}: Verweis {target} ins Leere");
                Check(schema.Count == 1, $"{where}: $ref mit weiteren Schlüsseln");
                return;
            }

            if (schema["type"] is { } type)
            {
                Check(type.GetValueKind() == JsonValueKind.String && Types.Contains(type.GetValue<string>()), $"{where}: type {type.ToJsonString()}");
            }

            if (schema["required"] is { } required)
            {
                var names = required.AsArray().Select(r => r!.GetValue<string>()).ToList();
                Check(names.Count > 0, $"{where}: required ist leer");
                Check(names.All(n => schema["properties"]?[n] is not null), $"{where}: required nennt unbekannte Eigenschaft");
            }

            if (schema.Text("type") == "array")
            {
                Check(schema["items"] is JsonObject, $"{where}: array ohne items");
            }

            if (schema["items"] is { } items)
            {
                Schema(items, $"{where}[]");
            }

            foreach (var (name, property) in schema["properties"]?.AsObject() ?? [])
            {
                Schema(property, $"{where}.{name}");
            }

            if (schema["additionalProperties"] is JsonObject additional)
            {
                Schema(additional, $"{where}{{}}");
            }

            foreach (var part in schema["allOf"]?.AsArray() ?? [])
            {
                Schema(part, $"{where} allOf");
            }
        }
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PathParameter();
}
