namespace Bfs.Seed.OpenApi.Tests;

/// <summary>Definitionen der Test-App: Records, Pflichtfelder, Nullbarkeit, Beschreibungen, Rekursion.</summary>
public class SchemaTests
{
    private static readonly SeedOpenApiResult App = TestDocuments.App;

    [Fact]
    public void Definitions_ForAllObjectTypesSorted()
    {
        Assert.Equal(
            ["Address", "ApiError", "Category", "CreateOrderRequest", "Customer", "HealthReport", "Order", "OrderLine", "PageOfOrder", "SeedRoleDetail", "SeedUserInfo"],
            App.Document["definitions"]!.AsObject().Select(d => d.Key));
    }

    [Fact]
    public void Records_UseCamelCaseAndTypesOfSystemTextJson()
    {
        var order = App.Properties("Order");

        Assert.Equal(["id", "customer", "lines", "state", "total", "createdAt", "note", "priority", "tags"], order.Select(p => p.Key));
        Assert.Equal("""{"type":"string","format":"uuid","description":"Bestell-ID."}""", order["id"]!.ToJsonString());
        Assert.Equal("""{"$ref":"#/definitions/Customer"}""", order["customer"]!.ToJsonString());
        Assert.Equal("""{"$ref":"#/definitions/OrderLine"}""", order["lines"]!["items"]!.ToJsonString());
        Assert.Equal("""{"type":"string","enum":["Open","Shipped","Cancelled"]}""", order["state"]!.ToJsonString());
        Assert.Equal("""{"type":"number","format":"double"}""", order["total"]!.ToJsonString());
        Assert.Equal("""{"type":"string","format":"date-time"}""", order["createdAt"]!.ToJsonString());
        Assert.Equal("string", order["tags"]!["additionalProperties"].Text("type"));
    }

    [Fact]
    public void Required_WithoutNullableConstructorParameters()
    {
        // Lücke (a): string? und int? sind keine Pflicht, auch wenn sie im Konstruktor stehen.
        Assert.Equal(["id", "customer", "lines", "state", "total", "createdAt"], App.Required("Order"));
        Assert.Equal(["name"], App.Required("Customer"));
        Assert.Equal(["items"], App.Required("PageOfOrder"));

        // required-Schlüsselwort bleibt Pflicht, Note nicht.
        Assert.Equal(["customer", "lines", "channel"], App.Required("CreateOrderRequest"));

        // Nur nullbare Eigenschaften: ohne required.
        Assert.Equal(["roles", "capabilities"], App.Required("SeedUserInfo"));
    }

    [Fact]
    public void Nullables_AreMarkedWithXNullable()
    {
        var order = App.Properties("Order");

        Assert.Equal("""{"type":"string","description":"Freitext der Kundin, null ohne Angabe.","x-nullable":true}""", order["note"]!.ToJsonString());
        Assert.Equal("""{"type":"integer","format":"int32","x-nullable":true}""", order["priority"]!.ToJsonString());

        // $ref mit Geschwistern ignoriert Swagger 2.0: Bei Objekten bleibt nur, dass sie keine Pflicht sind.
        Assert.Equal("""{"$ref":"#/definitions/Address"}""", App.Properties("Customer")["address"]!.ToJsonString());
    }

    [Fact]
    public void Descriptions_FromDescriptionAttributeBeforeXmlDocumentation()
    {
        // Lücke (b): XML-Doku von Record-Parametern, Eigenschaften und primären Konstruktoren.
        Assert.Equal("Positionen in Reihenfolge der Eingabe", App.Properties("Order")["lines"].Text("description"));
        Assert.Equal("Zusätzliche Angaben als Schlüssel und Wert, siehe Note.", App.Properties("Order")["tags"].Text("description"));
        Assert.Equal("Preis je Stück in Euro", App.Properties("OrderLine")["unitPrice"].Text("description"));
        Assert.Equal("Name des Kunden.", App.Properties("CreateOrderRequest")["customer"].Text("description"));
        Assert.Equal("Herkunft der Bestellung.", App.Properties("CreateOrderRequest")["channel"].Text("description"));
        Assert.Equal("Name der Kategorie.", App.Properties("Category")["name"].Text("description"));
        Assert.Equal("Adresse der nächsten Seite, fehlt auf der letzten.", App.Properties("PageOfOrder")["nextLink"].Text("description"));

        // Die summary eines Typs ist meist für Entwickler geschrieben und bleibt draußen.
        Assert.Null(App.Definition("Order")["description"]);
        Assert.Null(App.Properties("OrderLine")["sku"]!["description"]);
    }

    [Fact]
    public void RecursiveTypes_ReferenceThemselves()
    {
        var category = App.Properties("Category");

        Assert.Equal("""{"type":"array","items":{"$ref":"#/definitions/Category"},"description":"Unterkategorien."}""", category["children"]!.ToJsonString());
    }

    [Fact]
    public void GenericTypes_GetReadableNames()
    {
        Assert.Equal("""{"$ref":"#/definitions/Order"}""", App.Properties("PageOfOrder")["items"]!["items"]!.ToJsonString());
        Assert.Equal("""{"$ref":"#/definitions/PageOfOrder"}""", App.Operation("/archive/{year}", "get")["responses"]!["200"]!["schema"]!.ToJsonString());
    }

    [Fact]
    public void TypesFromOtherAssemblies_IncludingInitProperties()
    {
        var user = App.Properties("SeedUserInfo");

        Assert.Equal("""{"$ref":"#/definitions/SeedRoleDetail"}""", user["roleDetails"]!["items"]!.ToJsonString());
        Assert.True(user["name"]!["x-nullable"]!.GetValue<bool>());
    }
}
