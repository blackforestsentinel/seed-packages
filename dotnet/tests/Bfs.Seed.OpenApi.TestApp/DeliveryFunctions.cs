using System.Text.Json.Serialization;
using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Bfs.Seed.OpenApi.TestApp;

/// <summary>
/// Enums mit und ohne Converter in Query, Pfad und Body, ein fremdes Enum (DayOfWeek),
/// veraltete Operationen und Capabilities an Klasse und Methode.
/// </summary>
[RequireCapability("deliveries.read")]
public sealed class DeliveryFunctions
{
    [Function("ListDeliveries")]
    [OpenApiOperation("ListDeliveries", "Lieferungen auflisten")]
    [OpenApiParameter("day", Type = typeof(DayOfWeek))]
    [OpenApiParameter("priority", Type = typeof(Priority?))]
    [OpenApiParameter("state", Type = typeof(DeliveryState))]
    [OpenApiResponse(200, typeof(IReadOnlyList<Delivery>))]
    public static IActionResult List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "deliveries")] HttpRequest request) => new OkResult();

    [Function("CreateDelivery")]
    [RequireCapability("deliveries.write")]
    [OpenApiOperation("CreateDelivery", "Lieferung anlegen")]
    [OpenApiRequestBody(typeof(CreateDeliveryRequest))]
    [OpenApiResponse(201, typeof(Delivery))]
    public static IActionResult Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "deliveries")] HttpRequest request) => new OkResult();

    // Pfadparameter mit Enum-Typ aus dem Methodenparameter.
    [Function("ListDeliveriesByPriority")]
    [OpenApiOperation("ListDeliveriesByPriority", "Lieferungen einer Priorität")]
    [OpenApiResponse(200, typeof(IReadOnlyList<Delivery>))]
    public static IActionResult ByPriority(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "deliveries/priority/{priority}")] HttpRequest request,
        Priority priority) => new OkResult();

    // Veraltet über [Obsolete]; der Text landet in der description.
    [Obsolete("Stattdessen ListDeliveries verwenden.")]
    [Function("ListShipments")]
    [OpenApiOperation("ListShipments", "Sendungen auflisten", Description = "Alle Sendungen.")]
    [OpenApiResponse(200, typeof(IReadOnlyList<Delivery>))]
    public static IActionResult ListShipments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "shipments")] HttpRequest request) => new OkResult();

    // Veraltet über das Attribut, ohne Hinweis.
    [Function("GetShipment")]
    [OpenApiOperation("GetShipment", "Sendung abrufen", Deprecated = true)]
    [OpenApiResponse(200, typeof(Delivery))]
    public static IActionResult GetShipment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "shipments/{id}")] HttpRequest request,
        Guid id) => new OkResult();
}

/// <summary>Ohne Converter: im JSON eine Zahl.</summary>
public enum Priority
{
    Low,
    Normal = 5,
    High = 10,
}

[JsonConverter(typeof(JsonStringEnumConverter<DeliveryState>))]
public enum DeliveryState
{
    Planned,
    [JsonStringEnumMemberName("in-transit")]
    InTransit,
    Delivered,
}

public sealed record Delivery(Guid Id, Priority Priority, Priority? Fallback, DeliveryState State, DayOfWeek Day, IReadOnlyList<Priority> History);

/// <param name="Priority">Dringlichkeit.</param>
public sealed record CreateDeliveryRequest(Priority Priority, DayOfWeek Day);
