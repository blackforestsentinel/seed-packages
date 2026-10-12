using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bfs.Seed.OpenApi;

/// <summary>
/// Erzeugt openapi.json (Swagger 2.0) aus den HTTP-Functions einer App-Assembly, ohne den
/// Functions-Host zu starten: Pfad und Methoden aus <c>[Function]</c> und <c>[HttpTrigger]</c>,
/// der Rest aus den OpenApi-Attributen. Functions aus anderen Assemblies (etwa der MCP-Endpunkt
/// aus Bfs.Seed.Mcp) und Functions ohne HTTP-Trigger fehlen. <c>host</c> bleibt leer; den Host
/// je Umgebung setzt die Pipeline.
/// </summary>
public static partial class SeedOpenApiDocument
{
    private const string FunctionAttribute = "Microsoft.Azure.Functions.Worker.FunctionAttribute";
    private const string HttpTriggerAttribute = "Microsoft.Azure.Functions.Worker.HttpTriggerAttribute";
    private const string AllowAnonymousAttribute = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";
    private const string SecurityName = "oauth2";

    /// <summary>Power Platform lehnt größere Definitionen ab.</summary>
    private const int MaxBytes = 1024 * 1024;

    /// <summary>Methoden, die Swagger 2.0 kennt, in der Reihenfolge im Dokument.</summary>
    private static readonly string[] SwaggerMethods = ["get", "put", "post", "delete", "options", "head", "patch"];

    private static readonly JsonDocumentOptions HostJsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Dokument für alle HTTP-Functions der Assembly.</summary>
    /// <exception cref="SeedOpenApiException">Eine operationId oder Methode und Pfad gibt es mehrfach.</exception>
    public static SeedOpenApiResult Generate(Assembly assembly, SeedOpenApiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Generate(assembly, LoadableTypes(assembly), options);
    }

    /// <summary>
    /// Schreibt openapi.json für die gebaute App und liest dafür den Routen-Präfix aus host.json
    /// neben der Assembly. Gibt die Hinweise als <c>Code: Text</c> zurück. Aufruf durch das
    /// Werkzeug Bfs.Seed.OpenApi.Export beim Build; Signatur deshalb nicht ändern.
    /// </summary>
    /// <exception cref="SeedOpenApiException">Eine operationId oder Methode und Pfad gibt es mehrfach.</exception>
    public static string[] Export(Assembly assembly, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        var options = new SeedOpenApiOptions();
        if (RoutePrefix(assembly) is { } prefix)
        {
            options.RoutePrefix = prefix;
        }

        var result = Generate(assembly, options);
        var json = result.ToJson();
        var warnings = result.Warnings.ToList();
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            warnings.Add(new(SeedOpenApiCodes.TooLarge, "openapi.json ist größer als 1 MB; der Custom Connector lehnt das ab"));
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, json, new UTF8Encoding(false));
        return [.. warnings.Select(w => w.ToString())];
    }

    /// <summary>Wie <see cref="Generate(Assembly, SeedOpenApiOptions?)"/>, aber nur für die angegebenen Typen (Tests).</summary>
    internal static SeedOpenApiResult Generate(Assembly assembly, IEnumerable<Type> types, SeedOpenApiOptions? options)
    {
        options ??= new SeedOpenApiOptions();

        var warnings = new List<SeedOpenApiWarning>();
        var schemas = new SwaggerSchemas(options.SerializerOptions, warnings);
        var paths = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var operationIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var function in FindHttpFunctions(types, warnings))
        {
            var path = "/" + RouteParameter().Replace(function.Route, m => "{" + m.Groups["name"].Value + "}");
            foreach (var method in function.Methods)
            {
                var operation = Operation(function, method, schemas, warnings, options);
                var id = operation["operationId"]!.GetValue<string>();
                if (!operationIds.TryAdd(id, function.Name))
                {
                    throw new SeedOpenApiException(SeedOpenApiCodes.DuplicateOperationId, $"operationId {id} gibt es mehrfach: Functions {operationIds[id]} und {function.Name}");
                }

                if (!paths.TryGetValue(path, out var item))
                {
                    paths[path] = item = [];
                }

                if (item.ContainsKey(method))
                {
                    throw new SeedOpenApiException(SeedOpenApiCodes.DuplicateOperation, $"{method.ToUpperInvariant()} {path} gibt es mehrfach, zuletzt in Function {function.Name}");
                }

                item[method] = operation;
            }
        }

        var info = assembly.GetCustomAttribute<OpenApiInfoAttribute>();
        var title = info?.Title ?? assembly.GetName().Name ?? "API";
        if (title.Length > 30)
        {
            warnings.Add(new(SeedOpenApiCodes.TooLong, $"Titel \"{title}\" ist länger als 30 Zeichen; der Custom Connector lehnt das ab"));
        }

        var infoNode = new JsonObject { ["title"] = title };
        if (info?.Description is { } description)
        {
            infoNode["description"] = description;
        }

        infoNode["version"] = Version(assembly);

        var document = new JsonObject
        {
            ["swagger"] = "2.0",
            ["info"] = infoNode,
            ["basePath"] = "/" + options.RoutePrefix.Trim('/'),
            ["schemes"] = new JsonArray("https"),
            ["consumes"] = new JsonArray("application/json"),
            ["produces"] = new JsonArray("application/json"),
            ["paths"] = new JsonObject([.. paths.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)Sorted(p.Value)))]),
            ["definitions"] = schemas.Definitions(),
        };

        if (options.OAuth2)
        {
            document["securityDefinitions"] = new JsonObject
            {
                [SecurityName] = new JsonObject
                {
                    ["type"] = "oauth2",
                    ["flow"] = "accessCode",
                    ["authorizationUrl"] = options.AuthorizationUrl,
                    ["tokenUrl"] = options.TokenUrl,
                    ["scopes"] = new JsonObject(),
                },
            };
        }

        return new SeedOpenApiResult(document, [.. warnings.Distinct()]);
    }

    private static JsonObject Operation(HttpFunction function, string method, SwaggerSchemas schemas, List<SeedOpenApiWarning> warnings, SeedOpenApiOptions options)
    {
        var where = function.Name;
        var annotation = function.Method.GetCustomAttribute<OpenApiOperationAttribute>();
        if (annotation is null)
        {
            warnings.Add(new(SeedOpenApiCodes.MissingOperation, $"{where}: ohne [OpenApiOperation]; operationId und summary sind der Function-Name"));
        }

        var id = annotation?.OperationId ?? function.Name;
        if (function.Methods.Count > 1)
        {
            id += char.ToUpperInvariant(method[0]) + method[1..];
        }

        if (!OperationId().IsMatch(id))
        {
            warnings.Add(new(SeedOpenApiCodes.OperationIdFormat, $"{where}: operationId {id} sollte mit einem Buchstaben beginnen und nur Buchstaben, Ziffern und _ enthalten"));
        }

        var summary = annotation?.Summary ?? function.Name;
        if (summary.Length > 80)
        {
            warnings.Add(new(SeedOpenApiCodes.TooLong, $"{where}: summary ist länger als 80 Zeichen"));
        }

        var operation = new JsonObject { ["summary"] = summary };
        if (annotation?.Description is { } description)
        {
            operation["description"] = description;
        }

        operation["operationId"] = id;

        var parameters = new JsonArray();
        var declared = new Dictionary<string, OpenApiParameterAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var annotated in function.Method.GetCustomAttributes<OpenApiParameterAttribute>())
        {
            declared.TryAdd(annotated.Name, annotated);
        }

        foreach (Match match in RouteParameter().Matches(function.Route))
        {
            var name = match.Groups["name"].Value;
            var rest = match.Groups["rest"].Value;
            // {id?} und {id=standard}: Swagger 2.0 kennt keine optionalen Pfadparameter.
            if (rest.EndsWith('?') || rest.Contains('=', StringComparison.Ordinal))
            {
                warnings.Add(new(SeedOpenApiCodes.RouteParameter, $"{where}: optionaler Routenparameter {name} ist in Swagger 2.0 Pflicht; für Aufrufe ohne ihn eine eigene Function anlegen"));
            }

            if (match.Groups["catchAll"].Success)
            {
                warnings.Add(new(SeedOpenApiCodes.RouteParameter, $"{where}: Catch-all-Parameter {name}; Schrägstriche im Wert kodiert der Connector als %2F"));
            }

            declared.Remove(name, out var annotated);
            var type = annotated?.Type
                ?? function.Method.GetParameters().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.ParameterType
                ?? ConstraintType(rest);
            var parameter = Parameter(name, "path", required: true, type, annotated, schemas, $"{where}.{name}");
            // Sonst kodiert der Connector Pfadparameter doppelt.
            parameter["x-ms-url-encoding"] = "single";
            parameters.Add(parameter);
        }

        foreach (var annotated in declared.Values)
        {
            parameters.Add(Parameter(annotated.Name, "query", annotated.Required, annotated.Type ?? typeof(string), annotated, schemas, $"{where}.{annotated.Name}"));
        }

        if (function.Method.GetCustomAttribute<OpenApiRequestBodyAttribute>() is { } body)
        {
            if (method is "get" or "delete" or "head")
            {
                warnings.Add(new(SeedOpenApiCodes.BodyNotSent, $"{where}: Body bei {method.ToUpperInvariant()} sendet der Connector nicht"));
            }

            var parameter = new JsonObject { ["name"] = "body", ["in"] = "body", ["required"] = body.Required };
            if (body.Description is not null)
            {
                parameter["description"] = body.Description;
            }

            parameter["schema"] = schemas.SchemaFor(body.Type);
            parameters.Add(parameter);
        }

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        var responses = new JsonObject();
        var annotatedResponses = function.Method.GetCustomAttributes<OpenApiResponseAttribute>().OrderBy(r => r.StatusCode).ToList();
        if (annotatedResponses.Count == 0)
        {
            warnings.Add(new(SeedOpenApiCodes.MissingResponse, $"{where}: ohne [OpenApiResponse]; Power Automate und Copilot Studio sehen keine Ausgaben"));
            responses["200"] = new JsonObject { ["description"] = "OK" };
        }

        foreach (var response in annotatedResponses)
        {
            var entry = new JsonObject { ["description"] = response.Description ?? ReasonPhrase(response.StatusCode) };
            if (response.Type is not null)
            {
                entry["schema"] = schemas.SchemaFor(response.Type);
            }

            responses[response.StatusCode.ToString(CultureInfo.InvariantCulture)] = entry;
        }

        operation["responses"] = responses;

        if (options.OAuth2 && !function.Anonymous)
        {
            operation["security"] = new JsonArray(new JsonObject { [SecurityName] = new JsonArray() });
        }

        if (annotation?.Visibility is { } visibility and not OpenApiVisibility.Default)
        {
            operation["x-ms-visibility"] = visibility.ToString().ToLowerInvariant();
        }

        return operation;
    }

    private static JsonObject Parameter(string name, string location, bool required, Type type, OpenApiParameterAttribute? annotated, SwaggerSchemas schemas, string where)
    {
        var parameter = new JsonObject { ["name"] = name, ["in"] = location, ["required"] = required };
        if (annotated?.Description is not null)
        {
            parameter["description"] = annotated.Description;
        }

        if (annotated?.Summary is not null)
        {
            parameter["x-ms-summary"] = annotated.Summary;
        }

        if (annotated?.Visibility is { } visibility and not OpenApiVisibility.Default)
        {
            parameter["x-ms-visibility"] = visibility.ToString().ToLowerInvariant();
        }

        foreach (var (key, value) in schemas.ParameterType(type, where))
        {
            parameter[key] = value?.DeepClone();
        }

        return parameter;
    }

    private sealed record HttpFunction(string Name, MethodInfo Method, string Route, IReadOnlyList<string> Methods, bool Anonymous);

    private static IEnumerable<HttpFunction> FindHttpFunctions(IEnumerable<Type> types, List<SeedOpenApiWarning> warnings)
    {
        var classes = types
            .Where(t => t.IsClass && !t.ContainsGenericParameters && t.GetCustomAttribute<OpenApiIgnoreAttribute>() is null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var type in classes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var method in methods.Where(m => m.GetCustomAttribute<OpenApiIgnoreAttribute>() is null).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                if (FindAttribute(method, FunctionAttribute) is not { } function)
                {
                    continue;
                }

                var trigger = method.GetParameters().Select(p => FindAttribute(p, HttpTriggerAttribute)).FirstOrDefault(a => a is not null);
                if (trigger is null)
                {
                    continue;
                }

                var name = (string)Property(function, "Name")!;
                var route = ((string?)Property(trigger, "Route") ?? name).Trim('/');
                var verbs = ((string[]?)Property(trigger, "Methods") ?? []).Select(v => v.ToLowerInvariant()).Distinct().ToList();
                if (verbs.Count == 0)
                {
                    warnings.Add(new(SeedOpenApiCodes.Methods, $"{name}: HttpTrigger ohne Methoden nimmt jede an; dokumentiert sind GET und POST"));
                    verbs = ["get", "post"];
                }

                foreach (var unknown in verbs.Except(SwaggerMethods).ToList())
                {
                    warnings.Add(new(SeedOpenApiCodes.Methods, $"{name}: Methode {unknown.ToUpperInvariant()} gibt es in Swagger 2.0 nicht, fehlt im Dokument"));
                    verbs.Remove(unknown);
                }

                if (Property(trigger, "AuthLevel")?.ToString() is { } level and not "Anonymous")
                {
                    warnings.Add(new(SeedOpenApiCodes.FunctionKey, $"{name}: AuthorizationLevel.{level} verlangt einen Function-Key, den der Connector nicht sendet"));
                }

                var anonymous = FindAttribute(method, AllowAnonymousAttribute) is not null || FindAttribute(type, AllowAnonymousAttribute) is not null;
                yield return new HttpFunction(name, method, route, verbs, anonymous);
            }
        }
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return [.. e.Types.OfType<Type>()];
        }
    }

    private static object? FindAttribute(ICustomAttributeProvider provider, string fullName) =>
        provider.GetCustomAttributes(true).FirstOrDefault(a => a.GetType().FullName == fullName);

    private static object? Property(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target);

    /// <summary>Methoden eines Pfads in fester Reihenfolge, damit openapi.json bei jedem Build gleich aussieht.</summary>
    private static JsonObject Sorted(JsonObject item) =>
        new([.. item.OrderBy(m => Array.IndexOf(SwaggerMethods, m.Key)).Select(m => KeyValuePair.Create(m.Key, m.Value?.DeepClone()))]);

    /// <summary>Typ aus Route-Constraints wie <c>:int</c>; ohne passenden Constraint string.</summary>
    private static Type ConstraintType(string constraints) =>
        constraints.Split(':', '=', '?').Select(c => c.Trim().ToLowerInvariant()).Select(c => c switch
        {
            "int" => typeof(int),
            "long" => typeof(long),
            "bool" => typeof(bool),
            "guid" => typeof(Guid),
            "datetime" => typeof(DateTime),
            "double" or "decimal" or "float" => typeof(double),
            _ => null,
        }).FirstOrDefault(t => t is not null) ?? typeof(string);

    /// <summary><c>extensions.http.routePrefix</c> aus host.json neben der Assembly, sonst null.</summary>
    private static string? RoutePrefix(Assembly assembly)
    {
        if (string.IsNullOrEmpty(assembly.Location))
        {
            return null;
        }

        var hostJson = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "host.json");
        if (!File.Exists(hostJson))
        {
            return null;
        }

        var prefix = JsonNode.Parse(File.ReadAllText(hostJson), documentOptions: HostJsonOptions)?["extensions"]?["http"]?["routePrefix"];
        return prefix is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>InformationalVersion ohne Anhang <c>+&lt;commit&gt;</c>.</summary>
    private static string Version(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "1.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => $"Status {statusCode}",
    };

    /// <summary>Routenparameter wie <c>{id}</c>, <c>{id:int}</c>, <c>{id?}</c>, <c>{*rest}</c>.</summary>
    [GeneratedRegex(@"\{(?<catchAll>\*{1,2})?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<rest>[^{}]*)\}")]
    private static partial Regex RouteParameter();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex OperationId();
}
