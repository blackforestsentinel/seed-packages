using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Bfs.Seed.OpenApi.TestApp;

/// <summary>Bestellung. Dieser Text ist für Entwickler und gehört nicht ins Schema.</summary>
/// <param name="Id">Bestell-ID.</param>
/// <param name="Customer">Kunde mit Anschrift.</param>
/// <param name="Lines">Positionen laut XML-Doku; das Attribut hat Vorrang.</param>
/// <param name="Note">Freitext der Kundin, <c>null</c> ohne Angabe.</param>
public sealed record Order(
    Guid Id,
    Customer Customer,
    [property: Description("Positionen in Reihenfolge der Eingabe")] IReadOnlyList<OrderLine> Lines,
    OrderState State,
    decimal Total,
    DateTimeOffset CreatedAt,
    string? Note,
    int? Priority)
{
    /// <summary>Zusätzliche Angaben als Schlüssel und Wert, siehe <see cref="Note"/>.</summary>
    public Dictionary<string, string> Tags { get; init; } = [];
}

public sealed record Customer(string Name, Address? Address);

public sealed record Address(string Street, string City);

public sealed record OrderLine(string Sku, int Quantity, [property: Description("Preis je Stück in Euro")] decimal UnitPrice);

[JsonConverter(typeof(JsonStringEnumConverter<OrderState>))]
public enum OrderState
{
    Open,
    Shipped,
    Cancelled,
}

/// <param name="Customer">Name des Kunden.</param>
public sealed record CreateOrderRequest(string Customer, IReadOnlyList<OrderLine> Lines, string? Note)
{
    /// <summary>Herkunft der Bestellung.</summary>
    public required string Channel { get; init; }
}

/// <summary>Seite einer Liste.</summary>
/// <param name="NextLink">Adresse der nächsten Seite, fehlt auf der letzten.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextLink);

/// <summary>Rekursiver Typ, Klasse mit primärem Konstruktor.</summary>
/// <param name="name">Name der Kategorie.</param>
/// <param name="children">Unterkategorien.</param>
public sealed class Category(string name, IReadOnlyList<Category> children)
{
    public string Name { get; } = name;

    public IReadOnlyList<Category> Children { get; } = children;
}

public sealed record ApiError(string Error);
