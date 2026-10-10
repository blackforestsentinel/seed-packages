using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Prüft die Argumente eines Werkzeugaufrufs gegen das JSON-Schema, das das SDK aus der Methode
/// erzeugt: Typen, Pflichtangaben, Aufzählungen, Längen, Grenzen und Muster, auch verschachtelt.
/// Unbekannte Argumente auf oberster Ebene sind immer ein Fehler. Was das Schema darüber hinaus
/// ausdrückt (etwa <c>anyOf</c> oder <c>$ref</c>), prüft erst die Bindung an die Methode.
/// </summary>
internal static class SeedMcpArgumentValidator
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    public static IReadOnlyList<string> Validate(JsonElement schema, IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        var errors = new List<string>();
        var values = arguments?.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal) ?? [];
        ValidateObject(schema, values, path: null, strict: true, errors);
        return errors;
    }

    private static void ValidateObject(
        JsonElement schema,
        IReadOnlyDictionary<string, JsonElement> values,
        string? path,
        bool strict,
        List<string> errors)
    {
        var properties = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : default;
        var known = properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal)
            : [];

        var closed = strict
            || (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False);
        if (closed)
        {
            foreach (var name in values.Keys.Where(k => !known.Contains(k)))
            {
                var allowed = known.Count == 0 ? "keine" : string.Join(", ", known.Order(StringComparer.Ordinal));
                errors.Add($"Unbekanntes Argument „{Join(path, name)}“ (erlaubt: {allowed}).");
            }
        }

        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()).OfType<string>())
            {
                if (!values.ContainsKey(name))
                {
                    errors.Add($"Pflichtargument „{Join(path, name)}“ fehlt.");
                }
            }
        }

        foreach (var (name, value) in values)
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(name, out var propertySchema))
            {
                ValidateValue(propertySchema, value, Join(path, name), errors);
            }
        }
    }

    private static void ValidateValue(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("type", out var type))
        {
            var types = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Select(t => t.GetString()).OfType<string>().ToArray()
                : type.GetString() is { } single ? [single] : [];
            if (types.Length > 0 && !types.Any(t => HasType(value, t)))
            {
                errors.Add($"„{path}“ muss vom Typ {string.Join(" oder ", types.Select(Describe))} sein.");
                return;
            }
        }

        if (schema.TryGetProperty("enum", out var allowedValues) && allowedValues.ValueKind == JsonValueKind.Array
            && !allowedValues.EnumerateArray().Any(a => JsonElement.DeepEquals(a, value)))
        {
            errors.Add($"„{path}“ muss einer dieser Werte sein: {string.Join(", ", allowedValues.EnumerateArray().Select(a => a.GetRawText()))}.");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                ValidateString(schema, value.GetString()!, path, errors);
                break;
            case JsonValueKind.Number:
                ValidateNumber(schema, value.GetDouble(), path, errors);
                break;
            case JsonValueKind.Array:
                ValidateArray(schema, value, path, errors);
                break;
            case JsonValueKind.Object:
                ValidateObject(schema, value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal), path, strict: false, errors);
                break;
        }
    }

    private static void ValidateString(JsonElement schema, string value, string path, List<string> errors)
    {
        var length = new StringInfo(value).LengthInTextElements;
        if (TryGetNumber(schema, "minLength", out var min) && length < min)
        {
            errors.Add($"„{path}“ muss mindestens {min} Zeichen lang sein.");
        }

        if (TryGetNumber(schema, "maxLength", out var max) && length > max)
        {
            errors.Add($"„{path}“ darf höchstens {max} Zeichen lang sein.");
        }

        if (schema.TryGetProperty("pattern", out var pattern) && pattern.GetString() is { } regex)
        {
            try
            {
                if (!Regex.IsMatch(value, regex, RegexOptions.None, PatternTimeout))
                {
                    errors.Add($"„{path}“ hat nicht das erwartete Format ({regex}).");
                }
            }
            catch (RegexMatchTimeoutException)
            {
                errors.Add($"„{path}“ ließ sich nicht gegen das erwartete Format prüfen.");
            }
        }
    }

    private static void ValidateNumber(JsonElement schema, double value, string path, List<string> errors)
    {
        if (TryGetNumber(schema, "minimum", out var min) && value < min)
        {
            errors.Add($"„{path}“ muss mindestens {Format(min)} sein.");
        }

        if (TryGetNumber(schema, "maximum", out var max) && value > max)
        {
            errors.Add($"„{path}“ darf höchstens {Format(max)} sein.");
        }

        if (TryGetNumber(schema, "exclusiveMinimum", out var exclusiveMin) && value <= exclusiveMin)
        {
            errors.Add($"„{path}“ muss größer als {Format(exclusiveMin)} sein.");
        }

        if (TryGetNumber(schema, "exclusiveMaximum", out var exclusiveMax) && value >= exclusiveMax)
        {
            errors.Add($"„{path}“ muss kleiner als {Format(exclusiveMax)} sein.");
        }
    }

    private static void ValidateArray(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var count = value.GetArrayLength();
        if (TryGetNumber(schema, "minItems", out var min) && count < min)
        {
            errors.Add($"„{path}“ braucht mindestens {min} Einträge.");
        }

        if (TryGetNumber(schema, "maxItems", out var max) && count > max)
        {
            errors.Add($"„{path}“ darf höchstens {max} Einträge haben.");
        }

        if (schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateValue(items, item, $"{path}[{index++}]", errors);
            }
        }
    }

    private static bool HasType(JsonElement value, string type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d) && decimal.Truncate(d) == d,
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "array" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    private static string Describe(string type) => type switch
    {
        "string" => "Text",
        "integer" => "ganze Zahl",
        "number" => "Zahl",
        "boolean" => "true/false",
        "array" => "Liste",
        "object" => "Objekt",
        "null" => "null",
        _ => type,
    };

    private static bool TryGetNumber(JsonElement schema, string name, out double number)
    {
        number = 0;
        return schema.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out number);
    }

    private static string Format(double number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Join(string? path, string name) => path is null ? name : $"{path}.{name}";
}
