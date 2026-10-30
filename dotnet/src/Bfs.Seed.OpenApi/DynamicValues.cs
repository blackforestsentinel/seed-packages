using System.Text.Json.Nodes;

namespace Bfs.Seed.OpenApi;

/// <summary>
/// <c>x-ms-dynamic-values</c> aus <see cref="OpenApiDynamicValuesAttribute"/> und die Prüfung gegen
/// das fertige Dokument: Die Quell-Operation muss es geben (sonst Fehler), ihre Parameter und die
/// Pfade in ihrer Antwort sollten passen (sonst Warnung).
/// </summary>
internal static class DynamicValues
{
    public const string Key = "x-ms-dynamic-values";

    /// <summary>Erweiterung für einen Parameter oder eine Eigenschaft, Schlüssel wie bei Microsoft.</summary>
    /// <exception cref="SeedOpenApiException">Ein Eintrag in Parameters hat nicht die Form name=wert.</exception>
    public static JsonObject Extension(OpenApiDynamicValuesAttribute attribute, string where)
    {
        var extension = new JsonObject { ["operationId"] = attribute.OperationId };
        if (attribute.ValuePath is not null)
        {
            extension["value-path"] = attribute.ValuePath;
        }

        if (attribute.ValueTitle is not null)
        {
            extension["value-title"] = attribute.ValueTitle;
        }

        if (attribute.ValueCollection is not null)
        {
            extension["value-collection"] = attribute.ValueCollection;
        }

        // Laut Microsoft Pflicht, auch ohne Einträge.
        var parameters = new JsonObject();
        foreach (var entry in attribute.Parameters)
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            var name = equals > 0 ? entry[..equals].Trim() : "";
            var value = equals > 0 ? entry[(equals + 1)..].Trim() : "";
            var reference = value.Length >= 2 && value.StartsWith('{') && value.EndsWith('}') ? value[1..^1].Trim() : null;
            if (name.Length == 0 || reference is "" || parameters.ContainsKey(name))
            {
                throw new SeedOpenApiException(SeedOpenApiCodes.DynamicValuesAttribute, $"{where}: Parameters von [OpenApiDynamicValues] enthält \"{entry}\"; erwartet ist je Parameter einmal name=wert oder name={{parameter}}");
            }

            parameters[name] = reference is null ? value : new JsonObject { ["parameter"] = reference };
        }

        extension["parameters"] = parameters;
        return extension;
    }

    /// <summary>
    /// Prüft jede Erweiterung an Parametern der Operationen und an Eigenschaften unter
    /// <c>definitions</c>. Läuft, wenn alle Operationen stehen, damit die Quelle auch später im Dokument stehen darf.
    /// </summary>
    /// <exception cref="SeedOpenApiException">Die genannte operationId gibt es nicht.</exception>
    public static void Check(JsonObject document, List<SeedOpenApiWarning> warnings) => new Checker(document, warnings).Run();

    private sealed class Checker(JsonObject document, List<SeedOpenApiWarning> warnings)
    {
        private readonly JsonObject definitions = document["definitions"] as JsonObject ?? [];

        private readonly Dictionary<string, JsonObject> operations = (document["paths"] as JsonObject ?? [])
            .SelectMany(p => p.Value!.AsObject().Select(m => m.Value!.AsObject()))
            .ToDictionary(o => Text(o, "operationId")!, StringComparer.Ordinal);

        public void Run()
        {
            foreach (var (id, operation) in operations)
            {
                foreach (var parameter in Parameters(operation))
                {
                    if (parameter[Key] is JsonObject extension)
                    {
                        One(extension, $"{id}.{Text(parameter, "name")}", operation);
                    }
                }
            }

            foreach (var (name, schema) in definitions)
            {
                Walk(schema, name);
            }
        }

        private void Walk(JsonNode? node, string where)
        {
            if (node is not JsonObject schema)
            {
                return;
            }

            if (schema[Key] is JsonObject extension)
            {
                // An Eigenschaften gilt ein Verweis für jede Operation, die den Typ nutzt; den prüft hier nichts.
                One(extension, where, null);
            }

            foreach (var (name, property) in schema["properties"] as JsonObject ?? [])
            {
                Walk(property, $"{where}.{name}");
            }

            Walk(schema["items"], $"{where}[]");
            Walk(schema["additionalProperties"], $"{where}{{}}");
        }

        private void One(JsonObject extension, string where, JsonObject? target)
        {
            var id = Text(extension, "operationId") ?? "";
            if (!operations.TryGetValue(id, out var source))
            {
                throw new SeedOpenApiException(SeedOpenApiCodes.DynamicValuesOperation, $"{where}: x-ms-dynamic-values nennt operationId {id}, die es im Dokument nicht gibt{Candidates(id)}");
            }

            var given = extension["parameters"] as JsonObject ?? [];
            var sourceParameters = Parameters(source).ToList();
            foreach (var (name, value) in given)
            {
                if (!sourceParameters.Any(p => Text(p, "name") == name))
                {
                    Warn($"{where}: {id} hat keinen Parameter {name}");
                }

                if (target is not null && Text(value, "parameter") is { } other && !Parameters(target).Any(p => Text(p, "name") == other))
                {
                    Warn($"{where}: Verweis auf Parameter {other}, den {Text(target, "operationId")} nicht hat");
                }
            }

            foreach (var name in sourceParameters.Where(p => p["required"]?.GetValue<bool>() == true).Select(p => Text(p, "name")!).Where(n => !given.ContainsKey(n)))
            {
                Warn($"{where}: Pflichtparameter {name} von {id} fehlt in Parameters");
            }

            Paths(extension, where, id, source);
        }

        /// <summary>value-collection, value-path und value-title gegen das Schema der ersten Antwort 2xx.</summary>
        private void Paths(JsonObject extension, string where, string id, JsonObject source)
        {
            var response = (source["responses"] as JsonObject ?? [])
                .Where(r => r.Key.StartsWith('2'))
                .OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => r.Value?["schema"])
                .FirstOrDefault(s => s is not null);
            var list = Resolve(response);
            if (list is null)
            {
                return;
            }

            if (Text(extension, "value-collection") is { } collection)
            {
                var (found, known) = Lookup(list, collection);
                if (!known)
                {
                    return;
                }

                if (found is null)
                {
                    Warn($"{where}: value-collection {collection} fehlt in der Antwort von {id}");
                    return;
                }

                list = found;
            }

            if (Text(list, "type") != "array")
            {
                Warn(Text(extension, "value-collection") is { } path
                    ? $"{where}: value-collection {path} ist in der Antwort von {id} keine Liste"
                    : $"{where}: Antwort von {id} ist keine Liste; value-collection fehlt");
                return;
            }

            if (Resolve(list["items"]) is not { } item)
            {
                return;
            }

            foreach (var key in new[] { "value-path", "value-title" })
            {
                if (Text(extension, key) is { } path && Lookup(item, path) is (null, true))
                {
                    Warn($"{where}: {key} {path} fehlt in den Elementen der Antwort von {id}");
                }
            }

            if (Text(extension, "value-path") is null && item["properties"] is JsonObject)
            {
                Warn($"{where}: value-path fehlt, die Elemente der Antwort von {id} sind Objekte");
            }
        }

        /// <summary>
        /// Folgt einem Pfad wie <c>data/items</c> über <c>properties</c>. Known false: Ein Objekt
        /// ohne bekannte Eigenschaften liegt im Weg, ob der Pfad passt, ist offen.
        /// </summary>
        private (JsonObject? Schema, bool Known) Lookup(JsonObject schema, string path)
        {
            JsonObject? current = schema;
            foreach (var segment in path.Split('/'))
            {
                if (current["properties"] is not JsonObject properties)
                {
                    return (null, Text(current, "type") is not (null or "object"));
                }

                if ((current = Resolve(properties[segment])) is null)
                {
                    return (null, true);
                }
            }

            return (current, true);
        }

        private JsonObject? Resolve(JsonNode? node) =>
            Text(node, "$ref") is { } reference && reference.StartsWith("#/definitions/", StringComparison.Ordinal)
                ? definitions[reference["#/definitions/".Length..]] as JsonObject
                : node as JsonObject;

        /// <summary>Ähnliche operationIds: andere Schreibweise oder mit angehängter Methode.</summary>
        private string Candidates(string id)
        {
            var candidates = operations.Keys
                .Where(k => k.StartsWith(id, StringComparison.OrdinalIgnoreCase) && (k.Length == id.Length || SeedOpenApiDocument.SwaggerMethods.Contains(k[id.Length..].ToLowerInvariant())))
                .Order(StringComparer.Ordinal)
                .ToList();
            return candidates.Count == 0 ? "" : $"; gemeint ist wohl {string.Join(" oder ", candidates)}";
        }

        private void Warn(string message) => warnings.Add(new(SeedOpenApiCodes.DynamicValuesMismatch, message));

        private static IEnumerable<JsonObject> Parameters(JsonObject operation) =>
            (operation["parameters"] as JsonArray ?? []).OfType<JsonObject>();

        private static string? Text(JsonNode? node, string key) =>
            node is JsonObject o && o[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
