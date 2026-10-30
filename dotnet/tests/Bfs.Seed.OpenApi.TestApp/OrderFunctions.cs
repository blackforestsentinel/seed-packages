using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Bfs.Seed.OpenApi.TestApp;

/// <summary>Geschützte Functions; eine ist per Methode anonym, eine fehlt per [OpenApiIgnore].</summary>
public sealed class OrderFunctions
{
    [Function("ListOrders")]
    [OpenApiOperation("ListOrders", "Bestellungen auflisten", Description = "Neueste zuerst.")]
    [OpenApiParameter("top", Type = typeof(int), Description = "Höchstens so viele", Summary = "Anzahl")]
    [OpenApiParameter("state", Type = typeof(OrderState), Visibility = OpenApiVisibility.Advanced)]
    [OpenApiParameter("customer", Required = true)]
    [OpenApiDynamicValues("customer", "ListCustomers", ValuePath = "name", ValueTitle = "name", ValueCollection = "items", Parameters = ["active=true"])]
    [OpenApiResponse(200, typeof(IReadOnlyList<Order>))]
    public static IActionResult List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders")] HttpRequest request) => new OkResult();

    [Function("GetOrder")]
    [OpenApiOperation("GetOrder", "Bestellung abrufen")]
    [OpenApiParameter("id", Description = "ID aus CreateOrder", Summary = "Bestell-ID")]
    [OpenApiResponse(200, typeof(Order), Description = "Bestellung")]
    [OpenApiResponse(404)]
    public static IActionResult Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id:guid}")] HttpRequest request,
        Guid id) => new OkResult();

    [Function("CreateOrder")]
    [OpenApiOperation("CreateOrder", "Bestellung anlegen", Visibility = OpenApiVisibility.Important)]
    [OpenApiRequestBody(typeof(CreateOrderRequest), Description = "Neue Bestellung")]
    [OpenApiResponse(201, typeof(Order))]
    [OpenApiResponse(400, typeof(ApiError), Description = "Eingabe ungültig")]
    public static IActionResult Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")] HttpRequest request) => new OkResult();

    // Zwei Methoden: operationId OrderPut und OrderDelete, gleicher Pfad wie GetOrder.
    [Function("ChangeOrder")]
    [OpenApiOperation("Order", "Bestellung ersetzen oder löschen")]
    [OpenApiResponse(204)]
    public static IActionResult Change(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "delete", Route = "orders/{id}/")] HttpRequest request,
        string id) => new OkResult();

    [Function("CountOrders")]
    [AllowAnonymous]
    [OpenApiOperation("CountOrders", "Bestellungen zählen")]
    [OpenApiResponse(200, typeof(int))]
    public static IActionResult Count(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/count")] HttpRequest request) => new OkResult();

    // Lücke (c): optionaler Routenparameter, die einzige Warnung dieser App.
    [Function("ListArchive")]
    [OpenApiOperation("ListArchive", "Archiv auflisten")]
    [OpenApiResponse(200, typeof(Page<Order>))]
    public static IActionResult Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "archive/{year:int?}")] HttpRequest request) => new OkResult();

    [Function("ListCategories")]
    [OpenApiOperation("ListCategories", "Kategorien abrufen")]
    [OpenApiResponse(200, typeof(Category))]
    public static IActionResult Categories(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "categories")] HttpRequest request) => new OkResult();

    // Quelle der Auswahllisten für Kunden (ListOrders, CreateOrderRequest); im Connector unsichtbar.
    [Function("ListCustomers")]
    [OpenApiOperation("ListCustomers", "Kunden auflisten", Visibility = OpenApiVisibility.Internal)]
    [OpenApiParameter("active", Type = typeof(bool))]
    [OpenApiResponse(200, typeof(Page<Customer>))]
    public static IActionResult Customers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers")] HttpRequest request) => new OkResult();

    [Function("Hidden")]
    [OpenApiIgnore]
    public static IActionResult Hidden(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hidden")] HttpRequest request) => new OkResult();
}

/// <summary>Fehlt ganz in openapi.json.</summary>
[OpenApiIgnore]
public sealed class DebugFunctions
{
    [Function("Debug")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "debug")] HttpRequest request) => new OkResult();
}

/// <summary>Kein HTTP-Trigger, fehlt in openapi.json.</summary>
public sealed class OrderQueueFunctions
{
    [Function("ProcessOrder")]
    public static void Process([QueueTrigger("orders")] string message)
    {
    }
}
