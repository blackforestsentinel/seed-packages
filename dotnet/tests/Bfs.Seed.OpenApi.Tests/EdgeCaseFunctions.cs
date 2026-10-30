using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

// Titel über 30 Zeichen: Warnung SEEDOPENAPI004 in jedem Dokument dieser Assembly.
[assembly: Bfs.Seed.OpenApi.OpenApiInfo("Sonderfälle mit einem viel zu langen Titel")]

namespace Bfs.Seed.OpenApi.Tests;

// Sonderfälle für den Generator; Tests erzeugen Dokumente nur für einzelne dieser Klassen.

public sealed class ConstraintFunctions
{
    [Function("Item")]
    [OpenApiOperation("Item", "Eintrag lesen oder schreiben")]
    [OpenApiParameter("top", Type = typeof(long), Description = "Höchstens so viele")]
    [OpenApiResponse(200, typeof(string))]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "PUT", Route = "/items/{id:int}/{flag:bool}/{day:datetime}/{name:minlength(2)}")] HttpRequest request) => new OkResult();
}

public sealed class RouteParameterFunctions
{
    [Function("Optional")]
    [OpenApiOperation("Optional", "Optional")]
    [OpenApiResponse(204)]
    public static IActionResult Optional(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "optional/{name?}")] HttpRequest request) => new OkResult();

    [Function("Default")]
    [OpenApiOperation("Default", "Mit Standardwert")]
    [OpenApiResponse(204)]
    public static IActionResult Default(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "default/{page=1}")] HttpRequest request) => new OkResult();

    [Function("CatchAll")]
    [OpenApiOperation("CatchAll", "Alles danach")]
    [OpenApiResponse(204)]
    public static IActionResult CatchAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "files/{*path}")] HttpRequest request) => new OkResult();
}

public sealed class UnannotatedFunctions
{
    [Function("Plain")]
    public static IActionResult Plain(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest request) => new OkResult();

    [Function("AnyMethod")]
    [OpenApiOperation("AnyMethod", "Ohne Methoden")]
    [OpenApiResponse(204)]
    public static IActionResult AnyMethod(
        [HttpTrigger(AuthorizationLevel.Anonymous, Route = "any")] HttpRequest request) => new OkResult();

    [Function("Trace")]
    [OpenApiOperation("Trace", "Unbekannte Methode")]
    [OpenApiResponse(204)]
    public static IActionResult Trace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "trace", Route = "trace")] HttpRequest request) => new OkResult();
}

public sealed class QuestionableFunctions
{
    [Function("BodyOnGet")]
    [OpenApiOperation("Body-On-Get", "Body bei GET")]
    [OpenApiRequestBody(typeof(Square))]
    [OpenApiParameter("filter", Type = typeof(Square))]
    [OpenApiResponse(200, typeof(Shape))]
    public static IActionResult BodyOnGet(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "questionable")] HttpRequest request) => new OkResult();
}

public sealed class CollidingNameFunctions
{
    [Function("Colliding")]
    [OpenApiOperation("Colliding", "Gleiche Namen")]
    [OpenApiResponse(200, typeof(First.Item))]
    [OpenApiResponse(201, typeof(Second.Item))]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "colliding")] HttpRequest request) => new OkResult();
}

public sealed class DuplicateIdFunctions
{
    [Function("One")]
    [OpenApiOperation("Same", "Eins")]
    [OpenApiResponse(204)]
    public static IActionResult One(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "one")] HttpRequest request) => new OkResult();

    [Function("Two")]
    [OpenApiOperation("same", "Zwei")]
    [OpenApiResponse(204)]
    public static IActionResult Two(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "two")] HttpRequest request) => new OkResult();
}

public sealed class DuplicateRouteFunctions
{
    [Function("First")]
    [OpenApiOperation("FirstRoute", "Erste")]
    [OpenApiResponse(204)]
    public static IActionResult First(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "same/{id}")] HttpRequest request) => new OkResult();

    [Function("Second")]
    [OpenApiOperation("SecondRoute", "Zweite")]
    [OpenApiResponse(204)]
    public static IActionResult Second(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "same/{id:int}")] HttpRequest request) => new OkResult();
}

// x-ms-dynamic-values: Quellen, Nutzer, Abweichungen und falsch verwendete Attribute.

public sealed class DynamicValuesSourceFunctions
{
    // Zwei Methoden: operationId ProcessesGet und ProcessesPost; Pfad- und Pflichtparameter.
    [Function("Processes")]
    [OpenApiOperation("Processes", "Prozesse auflisten", Visibility = OpenApiVisibility.Internal)]
    [OpenApiParameter("mode", Required = true)]
    [OpenApiResponse(200, typeof(IReadOnlyList<ProcessInfo>))]
    public static IActionResult Processes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "projects/{projectId}/processes")] HttpRequest request) => new OkResult();

    [Function("ListWritableProcesses")]
    [OpenApiOperation("ListWritableProcesses", "Beschreibbare Prozesse", Visibility = OpenApiVisibility.Internal)]
    [OpenApiResponse(200, typeof(ProcessPage))]
    public static IActionResult Writable(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "processes/writable")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesFunctions
{
    [Function("CreateTask")]
    [OpenApiOperation("CreateTask", "Aufgabe anlegen")]
    [OpenApiParameter("processId", Required = true)]
    [OpenApiDynamicValues("processId", "ProcessesGet", ValuePath = "id", ValueTitle = "title", Parameters = ["projectId={projectId}", " mode = write "])]
    [OpenApiRequestBody(typeof(TaskRequest))]
    [OpenApiResponse(204)]
    public static IActionResult CreateTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "projects/{projectId}/tasks")] HttpRequest request) => new OkResult();

    [Function("GetProcess")]
    [OpenApiOperation("GetProcess", "Prozess abrufen")]
    [OpenApiDynamicValues("processId", "ListWritableProcesses", ValuePath = "id", ValueTitle = "title", ValueCollection = "value")]
    [OpenApiResponse(200, typeof(ProcessInfo))]
    public static IActionResult GetProcess(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "processes/{processId}")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesMismatchFunctions
{
    [Function("Mismatch")]
    [OpenApiOperation("Mismatch", "Passt nicht")]
    [OpenApiParameter("a")]
    [OpenApiParameter("b")]
    [OpenApiParameter("c")]
    [OpenApiParameter("d")]
    [OpenApiDynamicValues("a", "ProcessesGet", ValuePath = "id", ValueCollection = "items", Parameters = ["projectId={project}", "limit=5"])]
    [OpenApiDynamicValues("b", "ProcessesGet", ValuePath = "key", ValueTitle = "title/text", Parameters = ["projectId=1", "mode=read"])]
    [OpenApiDynamicValues("c", "ListWritableProcesses", ValuePath = "id")]
    [OpenApiDynamicValues("d", "ListWritableProcesses", ValueCollection = "value")]
    [OpenApiResponse(204)]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mismatch")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesUnknownFunctions
{
    // Die Function nimmt zwei Methoden an; gemeint ist ProcessesGet.
    [Function("Pick")]
    [OpenApiOperation("Pick", "Prozess wählen")]
    [OpenApiParameter("processId")]
    [OpenApiDynamicValues("processId", "Processes", ValuePath = "id", ValueTitle = "title")]
    [OpenApiResponse(204)]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "pick")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesUnknownInBodyFunctions
{
    [Function("Assign")]
    [OpenApiOperation("Assign", "Prozess zuordnen")]
    [OpenApiRequestBody(typeof(AssignRequest))]
    [OpenApiResponse(204)]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "assign")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesWrongParameterFunctions
{
    [Function("Wrong")]
    [OpenApiOperation("Wrong", "Falscher Name")]
    [OpenApiParameter("processId")]
    [OpenApiDynamicValues("process", "ListWritableProcesses")]
    [OpenApiResponse(204)]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wrong")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesWithoutParameterFunctions
{
    [Function("Nameless")]
    [OpenApiOperation("Nameless", "Ohne Parameternamen")]
    [OpenApiDynamicValues("ListWritableProcesses")]
    [OpenApiResponse(204)]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nameless")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesMalformedFunctions
{
    [Function("Malformed")]
    [OpenApiOperation("Malformed", "Parameters ohne =")]
    [OpenApiParameter("processId")]
    [OpenApiDynamicValues("processId", "ListWritableProcesses", Parameters = ["mode"])]
    [OpenApiResponse(204)]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "malformed")] HttpRequest request) => new OkResult();
}

public sealed class DynamicValuesNamedPropertyFunctions
{
    [Function("Named")]
    [OpenApiOperation("Named", "Eigenschaft mit Parameternamen")]
    [OpenApiRequestBody(typeof(NamedRequest))]
    [OpenApiResponse(204)]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "named")] HttpRequest request) => new OkResult();
}

public sealed record ProcessInfo(string Id, string Title);

public sealed record ProcessPage(IReadOnlyList<ProcessInfo> Value, string? NextLink);

public sealed record TaskRequest(
    string Title,
    [property: OpenApiDynamicValues("ProcessesGet", ValuePath = "id", ValueTitle = "title", Parameters = ["projectId={projectId}", "mode=read"])] string? FollowUpProcessId);

public sealed record AssignRequest([property: OpenApiDynamicValues("ListWritableProcesses", ValuePath = "id")] string ProcessId);

public sealed record NamedRequest([property: OpenApiDynamicValues("processId", "ListWritableProcesses")] string ProcessId);

[JsonDerivedType(typeof(Circle), "circle")]
[JsonDerivedType(typeof(Square), "square")]
public abstract record Shape;

public sealed record Circle(double Radius) : Shape;

public sealed record Square(double Side) : Shape;

public static class First
{
    public sealed record Item(string Name);
}

public static class Second
{
    public sealed record Item(int Number);
}
