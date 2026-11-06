using System.ComponentModel;
using System.Globalization;
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

    /// <summary>Anzeigetexte für Werte aus <c>enum</c>; die Power Platform zeigt sie in der Auswahl.</summary>
    internal const string EnumValuesKey = "x-ms-enum-values";

    private readonly Dictionary<Type, string> names = [];
    private readonly Dictionary<string, JsonNode> definitions = new(StringComparer.Ordinal);
    private readonly Queue<Type> pending = new();
    private readonly XmlDocumentation documentation = new();
    private readonly NullabilityInfoContext nullability = new();

    // SEEDOPENAPI014 nur, wo der Connector Werte sendet: Parameter und Anfrage-Body. Definitionen
    // entstehen einmal, auch wenn sie zuerst in einer Antwort vorkommen; ihre Warnungen warten
    // deshalb, bis feststeht, welche Definitionen ein Anfrage-Body erreicht.
    private readonly List<(string Definition, SeedOpenApiWarning Warning)> definitionEnumWarnings = [];
    private readonly HashSet<string> requestDefinitions = new(StringComparer.Ordinal);
    private string? exporting;
    private bool exportingRequest;

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

    /// <summary>Schema für Body (<paramref name="request"/>) oder Antwort: Objekte als <c>$ref</c>, alles andere inline.</summary>
    public JsonNode SchemaFor(Type type, bool request = false)
    {
        (exporting, exportingRequest) = (null, request);
        var node = IsDefinition(serializerOptions.GetTypeInfo(type)) ? Ref(type) : Export(type);
        if (request)
        {
            requestDefinitions.UnionWith(References(node));
        }

        while (pending.TryDequeue(out var next))
        {
            exporting = names[next];
            definitions[exporting] = Export(next);
        }

        exporting = null;
        return node;
    }

    /// <summary>
    /// Warnungen zu Enums ohne Werteliste in Definitionen, die ein Anfrage-Body direkt oder über
    /// andere Definitionen erreicht. Aufruf, wenn alle Operationen stehen.
    /// </summary>
    public void AddRequestEnumWarnings()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(requestDefinitions);
        while (queue.TryDequeue(out var name))
        {
            if (reachable.Add(name) && definitions.TryGetValue(name, out var schema))
            {
                foreach (var reference in References(schema))
                {
                    queue.Enqueue(reference);
                }
            }
        }

        warnings.AddRange(definitionEnumWarnings.Where(w => reachable.Contains(w.Definition)).Select(w => w.Warning));
    }

    /// <summary>Typ und Format für Pfad- und Query-Parameter, die in Swagger 2.0 kein Schema haben.</summary>
    public JsonObject ParameterType(Type type, string where)
    {
        // Objekte und Listen gar nicht erst exportieren: Ihre Definitionen würden sonst ungenutzt im Dokument landen.
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum && !IsFlags(underlying))
        {
            return EnumParameter(underlying, where);
        }

        // Listen und [Flags] wie bisher: Ein Wert aus einer Auswahl passt nicht zu mehreren Werten.
        (exporting, exportingRequest) = (null, true);
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

    /// <summary>
    /// Pfad und Query sind Text, kein JSON: Die Function liest ein Enum mit <c>Enum.TryParse</c> und
    /// kennt nur die Namen im Code. Deshalb immer string mit diesen Namen, unabhängig vom JsonConverter
    /// und von <c>[JsonStringEnumMemberName]</c>.
    /// </summary>
    private JsonObject EnumParameter(Type enumType, string where)
    {
        var result = new JsonObject { ["type"] = "string" };
        var names = Enum.GetNames(enumType);
        if (names.Length == 0)
        {
            warnings.Add(EnumWithoutValues(where, enumType, "es hat keine Werte"));
            return result;
        }

        result["enum"] = new JsonArray([.. names.Select(n => (JsonNode)n)]);
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

        // [Flags] erlaubt Kombinationen; eine Liste der Einzelwerte würde gültige Werte ausschließen.
        var enumType = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;
        var listedEnum = enumType.IsEnum && !IsFlags(enumType);
        if (node is not JsonObject schema)
        {
            // Eigener JsonConverter am Enum: Das Schema ist offen (true), Werte kennt nur der Converter.
            if (listedEnum)
            {
                EnumWarning(EnumWithoutValues(PropertyName(context), enumType, "System.Text.Json nennt keine Werte, etwa bei einem eigenen JsonConverter"));
            }

            return node;
        }

        if (listedEnum)
        {
            AddEnumValues(schema, enumType, PropertyName(context));
        }

        // Liste von Enums mit eigenem JsonConverter: Für die Elemente liefert System.Text.Json kein Schema.
        if (context.TypeInfo is { Kind: JsonTypeInfoKind.Enumerable, ElementType: { } element }
            && (Nullable.GetUnderlyingType(element) ?? element) is { IsEnum: true } itemEnum
            && !IsFlags(itemEnum)
            && schema["items"] is not JsonObject)
        {
            EnumWarning(EnumWithoutValues(PropertyName(context), itemEnum, "System.Text.Json nennt keine Werte, etwa bei einem eigenen JsonConverter"));
        }

        if (Format(context.TypeInfo.Type) is { } format && !schema.ContainsKey("format"))
        {
            schema["format"] = format;
        }

        if (Description(context) is { } description)
        {
            schema["description"] = description;
        }

        if (DynamicValuesAttribute(context.PropertyInfo) is { } dynamicValues)
        {
            schema[DynamicValues.Key] = DynamicValues.Extension(dynamicValues, $"{context.PropertyInfo!.DeclaringType.Name}.{context.PropertyInfo.Name}");
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
    /// Enum im JSON: Mit JsonStringEnumConverter liefert System.Text.Json die Namen schon als <c>enum</c>.
    /// Ohne Converter ist es eine Zahl ohne Liste; dann kommen die Zahlen als <c>enum</c> dazu und die
    /// Namen als Anzeigetext in <c>x-ms-enum-values</c>, sonst zeigt der Connector nur ein Zahlenfeld.
    /// </summary>
    private void AddEnumValues(JsonObject schema, Type enumType, string? where)
    {
        if (schema["enum"] is JsonArray { Count: > 0 } existing && existing.Any(v => v is not null))
        {
            return;
        }

        var integer = schema["type"] switch
        {
            JsonValue type => type.GetValue<string>() == "integer",
            JsonArray types => types.Any(t => t?.GetValue<string>() == "integer"),
            _ => false,
        };
        if (!integer)
        {
            EnumWarning(EnumWithoutValues(where, enumType, "System.Text.Json nennt keine Werte"));
            return;
        }

        // Gleiche Zahl unter mehreren Namen: Die Power Platform erlaubt je Wert nur einen Anzeigetext; der erste gilt.
        var names = Enum.GetNames(enumType);
        var values = Enum.GetValuesAsUnderlyingType(enumType);
        var seen = new HashSet<object>();
        var enumValues = new JsonArray();
        var displayValues = new JsonArray();
        for (var i = 0; i < names.Length; i++)
        {
            var value = values.GetValue(i)!;
            if (seen.Add(value))
            {
                enumValues.Add(Number(value));
                displayValues.Add(new JsonObject { ["value"] = Number(value), ["displayName"] = names[i] });
            }
        }

        if (enumValues.Count == 0)
        {
            EnumWarning(EnumWithoutValues(where, enumType, "es hat keine Werte"));
            return;
        }

        schema["enum"] = enumValues;
        schema[EnumValuesKey] = displayValues;
    }

    private static JsonNode Number(object value) =>
        value is ulong large ? JsonValue.Create(large) : JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));

    /// <summary>Im Anfrage-Body oder an einem Parameter sofort, in einer Definition später, in einer Antwort nie.</summary>
    private void EnumWarning(SeedOpenApiWarning warning)
    {
        if (exporting is not null)
        {
            definitionEnumWarnings.Add((exporting, warning));
        }
        else if (exportingRequest)
        {
            warnings.Add(warning);
        }
    }

    private static bool IsFlags(Type enumType) => enumType.IsDefined(typeof(FlagsAttribute), inherit: false);

    /// <summary>Namen der Definitionen, auf die ein Schema per <c>$ref</c> verweist.</summary>
    private static IEnumerable<string> References(JsonNode? node) => node switch
    {
        JsonObject o when o["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var target) && target.StartsWith("#/definitions/", StringComparison.Ordinal)
            => [target["#/definitions/".Length..]],
        JsonObject o => o.SelectMany(p => References(p.Value)),
        JsonArray a => a.SelectMany(References),
        _ => [],
    };

    private static SeedOpenApiWarning EnumWithoutValues(string? where, Type enumType, string reason) =>
        new(SeedOpenApiCodes.EnumWithoutValues, $"{(where is null ? "" : where + ": ")}Enum {enumType.Name} ohne Werteliste, {reason}; der Connector zeigt keine Auswahl");

    private static string? PropertyName(JsonSchemaExporterContext context) =>
        context.PropertyInfo is { } property ? $"{property.DeclaringType.Name}.{property.Name}" : null;

    /// <summary><see cref="OpenApiDynamicValuesAttribute"/> an der Eigenschaft, höchstens eines und ohne Parameternamen.</summary>
    private static OpenApiDynamicValuesAttribute? DynamicValuesAttribute(JsonPropertyInfo? property)
    {
        var attributes = property?.AttributeProvider?.GetCustomAttributes(typeof(OpenApiDynamicValuesAttribute), true) ?? [];
        if (attributes is not [OpenApiDynamicValuesAttribute attribute, ..])
        {
            return null;
        }

        if (attributes.Length > 1 || attribute.Parameter is not null)
        {
            throw new SeedOpenApiException(SeedOpenApiCodes.DynamicValuesAttribute, $"{property!.DeclaringType.Name}.{property.Name}: an einer Eigenschaft gibt es [OpenApiDynamicValues] nur einmal und ohne Parameternamen, etwa [property: OpenApiDynamicValues(\"{attribute.OperationId}\")]");
        }

        return attribute;
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
        else if (schema["type"] is JsonValue arrayType && arrayType.TryGetValue<string>(out var typeName) && typeName == "array")
        {
            // Beliebige Elemente (etwa mit eigenem JsonConverter) lässt System.Text.Json ohne items; Swagger 2.0 verlangt es.
            schema["items"] = new JsonObject();
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
