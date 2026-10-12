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
