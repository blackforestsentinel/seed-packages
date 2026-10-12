using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Bfs.Seed.OpenApi;

/// <summary>
/// Schemas nach Swagger 2.0 aus <see cref="JsonSchemaExporter"/>: Objekttypen landen unter
/// <c>definitions</c> und werden per <c>$ref</c> eingebunden; was Swagger 2.0 nicht kennt
/// (Typ-Listen mit null, anyOf, const), wird umgeschrieben oder fällt weg.
/// </summary>
internal sealed class SwaggerSchemas(JsonSerializerOptions serializerOptions, List<SeedOpenApiWarning> warnings)
{
    private static readonly string[] UnsupportedKeywords =
        ["$schema", "$id", "$comment", "$anchor", "$defs", "prefixItems", "unevaluatedProperties", "dependentRequired", "propertyNames"];

    private static readonly string[] ParameterKeywords = ["type", "format", "enum", "pattern"];

    private readonly Dictionary<Type, string> names = [];
    private readonly Dictionary<string, JsonNode> definitions = new(StringComparer.Ordinal);
    private readonly Queue<Type> pending = new();
    private readonly XmlDocumentation documentation = new();
    private readonly NullabilityInfoContext nullability = new();

    /// <summary>Alle bisher erzeugten Definitionen, nach Namen sortiert.</summary>
    public JsonObject Definitions()
    {
        var result = new JsonObject();
        foreach (var (name, schema) in definitions.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            result[name] = schema.DeepClone();
        }

        return result;
    }

    /// <summary>Schema für Body oder Antwort: Objekte als <c>$ref</c>, alles andere inline.</summary>
    public JsonNode SchemaFor(Type type)
    {
        var node = IsDefinition(serializerOptions.GetTypeInfo(type)) ? Ref(type) : Export(type);
        while (pending.TryDequeue(out var next))
        {
            definitions[names[next]] = Export(next);
        }

        return node;
    }

    /// <summary>Typ und Format für Pfad- und Query-Parameter, die in Swagger 2.0 kein Schema haben.</summary>
    public JsonObject ParameterType(Type type, string where)
    {
        // Objekte und Listen gar nicht erst exportieren: Ihre Definitionen würden sonst ungenutzt im Dokument landen.
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var schema = serializerOptions.GetTypeInfo(underlying).Kind == JsonTypeInfoKind.None ? Export(underlying) : new JsonObject();
        var result = new JsonObject();
        var kind = schema["type"]?.GetValue<string>();
        if (kind is null or "object" or "array")
        {
            warnings.Add(new(SeedOpenApiCodes.ParameterType, $"{where}: Typ {type.Name} ist als Parameter nicht abbildbar, verwende string"));
            result["type"] = "string";
            return result;
        }

        foreach (var key in ParameterKeywords)
        {
            if (schema[key] is { } value)
            {
                result[key] = value.DeepClone();
            }
        }

        return result;
    }

    private JsonNode Export(Type type)
    {
        var node = serializerOptions.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Transform,
        });
        return ToSwagger(node, type.Name);
    }

    private JsonNode Transform(JsonSchemaExporterContext context, JsonNode node)
    {
        // Verschachtelte Objekte werden zu Definitionen; die Wurzel bleibt, sie ist die Definition
        // selbst, ebenso abgeleitete Typen (anyOf), von denen ToSwagger den ersten übernimmt.
        if (context.Path.Length > 0 && context.BaseTypeInfo is null && IsDefinition(context.TypeInfo))
        {
            return Ref(context.TypeInfo.Type);
        }

        if (node is not JsonObject schema)
        {
            return node;
        }

        if (Format(context.TypeInfo.Type) is { } format && !schema.ContainsKey("format"))
        {
            schema["format"] = format;
        }

        if (Description(context) is { } description)
        {
            schema["description"] = description;
        }

        if (context.PropertyInfo is null && context.TypeInfo.Kind == JsonTypeInfoKind.Object)
        {
            RemoveNullablesFromRequired(schema, context.TypeInfo);
        }

        return schema;
    }

    /// <summary>
    /// Vorrang hat <c>[Description]</c> an Eigenschaft, Konstruktorparameter oder (nur an der
    /// Wurzel) am Typ; sonst die XML-Doku der Eigenschaft oder des Record-Parameters.
    /// </summary>
    private string? Description(JsonSchemaExporterContext context)
    {
        var property = context.PropertyInfo;
        var parameter = property?.AssociatedParameter?.AttributeProvider as ParameterInfo;
        return DescriptionAttribute(property?.AttributeProvider)
            ?? DescriptionAttribute(parameter)
            ?? (context.Path.Length == 0 ? DescriptionAttribute(context.TypeInfo.Type) : null)
            ?? (property?.AttributeProvider is MemberInfo member ? documentation.Summary(member) : null)
            ?? (parameter is not null ? documentation.Parameter(parameter) : null);
    }

    /// <summary>
    /// JsonSchemaExporter nennt jeden Konstruktorparameter ohne Standardwert als Pflicht, auch
    /// <c>string?</c>. Für den Connector ist Pflicht, was einen Wert haben muss: Nullbare
    /// Konstruktorparameter fallen heraus; <c>required</c> und <c>[JsonRequired]</c> bleiben.
    /// </summary>
    private void RemoveNullablesFromRequired(JsonObject schema, JsonTypeInfo typeInfo)
    {
        if (schema["required"] is not JsonArray required)
        {
            return;
        }

        var optional = typeInfo.Properties
            .Where(p => !p.IsRequired && IsNullable(p))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var remaining = required.Select(r => r!.GetValue<string>()).Where(r => !optional.Contains(r)).ToList();
        if (remaining.Count == 0)
        {
            schema.Remove("required");
        }
        else if (remaining.Count < required.Count)
        {
            schema["required"] = new JsonArray([.. remaining.Select(r => (JsonNode)r)]);
        }
    }

    /// <summary>Nullbar wie im Schema: <c>T?</c> und <c>Nullable&lt;T&gt;</c>; ohne Annotationen nicht nullbar.</summary>
    private bool IsNullable(JsonPropertyInfo property)
    {
        var info = (property.AssociatedParameter?.AttributeProvider ?? property.AttributeProvider) switch
        {
            ParameterInfo p => nullability.Create(p),
            PropertyInfo p => nullability.Create(p),
            FieldInfo f => nullability.Create(f),
            _ => null,
        };
        return info?.ReadState == NullabilityState.Nullable;
    }

    private JsonObject Ref(Type type)
    {
        if (!names.TryGetValue(type, out var name))
        {
            var baseName = DefinitionName(type);
            name = baseName;
            for (var i = 2; names.ContainsValue(name); i++)
            {
                name = $"{baseName}{i}";
            }

            if (name != baseName)
            {
                warnings.Add(new(SeedOpenApiCodes.DefinitionName, $"Definition {baseName} gibt es mehrfach, {type.FullName} heißt {name}"));
            }

            names[type] = name;
            pending.Enqueue(type);
        }

        return new JsonObject { ["$ref"] = $"#/definitions/{name}" };
    }

    /// <summary>Wandelt JSON Schema (draft 2020-12) in das Schema-Objekt von Swagger 2.0.</summary>
    private JsonNode ToSwagger(JsonNode? node, string where)
    {
        // true (beliebiger Wert) kennt Swagger 2.0 nur als leeres Schema.
        if (node is not JsonObject schema)
        {
            return new JsonObject();
        }

        foreach (var key in UnsupportedKeywords)
        {
            schema.Remove(key);
        }

        if (schema["$ref"]?.GetValue<string>() is { } reference && !reference.StartsWith("#/definitions/", StringComparison.Ordinal))
        {
            warnings.Add(new(SeedOpenApiCodes.SchemaNotMappable, $"{where}: Verweis {reference} ist in Swagger 2.0 nicht abbildbar, Schema offen gelassen"));
            return new JsonObject();
        }

        var nullable = false;
        if (schema["type"] is JsonArray types)
        {
            var rest = types.Select(t => t!.GetValue<string>()).Where(t => t != "null").ToList();
            nullable = rest.Count < types.Count;
            if (rest.Count > 1)
            {
                warnings.Add(new(SeedOpenApiCodes.SchemaNotMappable, $"{where}: mehrere Typen ({string.Join(", ", rest)}), verwende {rest[0]}"));
            }

            schema["type"] = rest.Count > 0 ? rest[0] : "string";
        }

        if (schema["const"] is { } constant)
        {
            schema.Remove("const");
            schema["enum"] = new JsonArray(constant.DeepClone());
        }

        if (schema["enum"] is JsonArray values && values.Any(v => v is null))
        {
            nullable = true;
            schema["enum"] = new JsonArray([.. values.Where(v => v is not null).Select(v => v!.DeepClone())]);
        }

        if (schema["enum"] is JsonArray enumValues && !schema.ContainsKey("type"))
        {
            schema.Insert(0, "type", enumValues.All(v => v?.GetValueKind() == JsonValueKind.String) ? "string" : "integer");
        }

        foreach (var key in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (schema[key] is JsonArray branches)
            {
                // Polymorphie und Ähnliches: Swagger 2.0 kennt nur allOf; nimm den ersten Zweig ohne null.
                warnings.Add(new(SeedOpenApiCodes.SchemaNotMappable, $"{where}: {key} ist für den Connector nicht abbildbar, verwende den ersten Zweig"));
                schema.Remove(key);
                var first = branches.OfType<JsonObject>().FirstOrDefault(b => b["type"]?.GetValue<string>() != "null");
                if (first is not null)
                {
                    Merge(schema, ToSwagger(first.DeepClone(), where).AsObject());
                }
            }
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var name in properties.Select(p => p.Key).ToList())
            {
                properties[name] = ToSwagger(properties[name], $"{where}.{name}");
            }
        }

        if (schema["items"] is { } items)
        {
            schema["items"] = ToSwagger(items, $"{where}[]");
        }

        if (schema["additionalProperties"] is JsonObject additional)
        {
            schema["additionalProperties"] = ToSwagger(additional, $"{where}{{}}");
        }

        // $ref mit Geschwistern ignoriert Swagger 2.0; die Nullbarkeit eines Objekts geht deshalb verloren.
        if (nullable && !schema.ContainsKey("$ref"))
        {
            schema["x-nullable"] = true;
        }

        return schema;
    }

    /// <summary>Übernimmt einen Zweig: Eigenschaften und Pflichtfelder zusammen, sonst nur, was fehlt.</summary>
    private static void Merge(JsonObject schema, JsonObject branch)
    {
        foreach (var (key, value) in branch)
        {
            switch (key, schema[key], value)
            {
                case ("properties", JsonObject properties, JsonObject more):
                    foreach (var (name, property) in more)
                    {
                        properties[name] ??= property?.DeepClone();
                    }

                    break;
                case ("required", JsonArray required, JsonArray more):
                    var names = required.Select(r => r!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                    foreach (var name in more.Select(r => r!.GetValue<string>()).Where(names.Add))
                    {
                        required.Add(name);
                    }

                    break;
                default:
                    schema[key] ??= value?.DeepClone();
                    break;
            }
        }
    }

    private static bool IsDefinition(JsonTypeInfo info) =>
        info.Kind == JsonTypeInfoKind.Object && info.Type != typeof(object);

    private static string DefinitionName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}Of{string.Concat(type.GetGenericArguments().Select(DefinitionName))}";
    }

    private static string? Format(Type type) => (Nullable.GetUnderlyingType(type) ?? type) switch
    {
        var t when t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(ushort) => "int32",
        var t when t == typeof(long) || t == typeof(uint) || t == typeof(ulong) => "int64",
        var t when t == typeof(float) => "float",
        var t when t == typeof(double) || t == typeof(decimal) => "double",
        _ => null,
    };

    private static string? DescriptionAttribute(ICustomAttributeProvider? provider) =>
        provider?.GetCustomAttributes(typeof(DescriptionAttribute), true) is [DescriptionAttribute d, ..] ? d.Description : null;
}
