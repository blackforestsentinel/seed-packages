namespace Bfs.Seed.OpenApi;

/// <summary>Sichtbarkeit einer Operation oder eines Parameters im Custom Connector (<c>x-ms-visibility</c>).</summary>
public enum OpenApiVisibility
{
    /// <summary>Ohne Angabe: normal sichtbar.</summary>
    Default,

    /// <summary>Immer zuerst angezeigt.</summary>
    Important,

    /// <summary>Unter „Erweiterte Optionen“ versteckt.</summary>
    Advanced,

    /// <summary>In Power Automate und Copilot Studio nicht sichtbar.</summary>
    Internal,
}

/// <summary>
/// Beschreibt eine HTTP-Function als Operation in openapi.json. Pfad, Methode und Routenparameter
/// kommen aus <c>[HttpTrigger]</c>, Antworten aus <see cref="OpenApiResponseAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OpenApiOperationAttribute(string operationId, string summary) : Attribute
{
    /// <summary>
    /// Eindeutige ID, im Connector der interne Name der Aktion, z. B. <c>GetJob</c>. Nimmt die
    /// Function mehrere Methoden an, hängt der Generator die Methode an (<c>ItemGet</c>, <c>ItemPut</c>).
    /// </summary>
    public string OperationId { get; } = operationId;

    /// <summary>Titel der Aktion in Power Automate und Copilot Studio, höchstens 80 Zeichen.</summary>
    public string Summary { get; } = summary;

    /// <summary>Beschreibung; Copilot Studio wählt Aktionen auch danach aus.</summary>
    public string? Description { get; set; }

    /// <summary>Sichtbarkeit im Connector.</summary>
    public OpenApiVisibility Visibility { get; set; }
}

/// <summary>Body der Anfrage als JSON des angegebenen Typs.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OpenApiRequestBodyAttribute(Type type) : Attribute
{
    /// <summary>Typ des Bodys; Schema nach System.Text.Json mit den Web-Standards (camelCase).</summary>
    public Type Type { get; } = type;

    /// <summary>Beschreibung des Bodys.</summary>
    public string? Description { get; set; }

    /// <summary>Pflicht, Standard ja.</summary>
    public bool Required { get; set; } = true;
}

/// <summary>Eine mögliche Antwort; mehrere Attribute für mehrere Statuscodes.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OpenApiResponseAttribute : Attribute
{
    /// <summary>Antwort ohne Body, z. B. 404.</summary>
    public OpenApiResponseAttribute(int statusCode) => StatusCode = statusCode;

    /// <summary>Antwort mit JSON-Body des angegebenen Typs.</summary>
    public OpenApiResponseAttribute(int statusCode, Type type) => (StatusCode, Type) = (statusCode, type);

    /// <summary>HTTP-Statuscode.</summary>
    public int StatusCode { get; }

    /// <summary>Typ des Bodys oder null ohne Body.</summary>
    public Type? Type { get; }

    /// <summary>Beschreibung; ohne Angabe der Standardtext des Statuscodes.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Parameter in der Route (Name kommt in der Route vor) oder in der Query. Routenparameter
/// erkennt der Generator auch ohne Attribut; das Attribut ergänzt Beschreibung oder Typ.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OpenApiParameterAttribute(string name) : Attribute
{
    /// <summary>Name wie in Route oder Query.</summary>
    public string Name { get; } = name;

    /// <summary>Typ, Standard string; bei Routenparametern sonst aus Methodenparameter oder Constraint.</summary>
    public Type? Type { get; set; }

    /// <summary>Pflicht; Routenparameter sind es immer.</summary>
    public bool Required { get; set; }

    /// <summary>Beschreibung.</summary>
    public string? Description { get; set; }

    /// <summary>Anzeigename im Connector (<c>x-ms-summary</c>).</summary>
    public string? Summary { get; set; }

    /// <summary>Sichtbarkeit im Connector.</summary>
    public OpenApiVisibility Visibility { get; set; }
}

/// <summary>
/// Auswahlliste statt freier Eingabe (<c>x-ms-dynamic-values</c>): Power Automate und Copilot Studio
/// zeigen die Werte, die eine andere Operation liefert. An der Function für einen Pfad- oder
/// Query-Parameter, an der Eigenschaft eines Body-Typs für dieses Feld (bei Records
/// <c>[property: OpenApiDynamicValues(…)]</c>). Pfade mit <c>/</c> getrennt, ohne führenden <c>/</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class OpenApiDynamicValuesAttribute : Attribute
{
    /// <summary>An der Eigenschaft eines Body-Typs: Werte aus der Operation <paramref name="operationId"/>.</summary>
    public OpenApiDynamicValuesAttribute(string operationId) => OperationId = operationId;

    /// <summary>An der Function: Werte für den Parameter <paramref name="parameter"/> aus der Operation <paramref name="operationId"/>.</summary>
    public OpenApiDynamicValuesAttribute(string parameter, string operationId) => (Parameter, OperationId) = (parameter, operationId);

    /// <summary>Name des Parameters wie in Route oder <see cref="OpenApiParameterAttribute"/>; an Eigenschaften null.</summary>
    public string? Parameter { get; }

    /// <summary>
    /// operationId der Operation, die die Werte liefert; nimmt deren Function mehrere Methoden an,
    /// mit angehängter Methode (<c>ItemGet</c>). Sie muss im Dokument stehen, am besten mit
    /// <see cref="OpenApiVisibility.Internal"/>.
    /// </summary>
    public string OperationId { get; }

    /// <summary>Pfad zum Wert in einem Element der Liste, etwa <c>id</c> (<c>value-path</c>).</summary>
    public string? ValuePath { get; set; }

    /// <summary>Pfad zum angezeigten Text in einem Element der Liste, etwa <c>name</c> (<c>value-title</c>).</summary>
    public string? ValueTitle { get; set; }

    /// <summary>Pfad zur Liste in der Antwort, etwa <c>items</c>; ohne Angabe ist die Antwort die Liste (<c>value-collection</c>).</summary>
    public string? ValueCollection { get; set; }

    /// <summary>
    /// Parameter für den Aufruf der Operation: <c>name=wert</c> mit festem Wert, <c>name={anderer}</c>
    /// mit dem Wert des Parameters <c>anderer</c> der Operation, in der die Auswahl steht.
    /// </summary>
    public string[] Parameters { get; set; } = [];
}

/// <summary>Nimmt eine HTTP-Function oder alle einer Klasse aus openapi.json heraus.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class OpenApiIgnoreAttribute : Attribute;

/// <summary>Titel und Beschreibung der API, einmal je App: <c>[assembly: OpenApiInfo("Titel")]</c>.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class OpenApiInfoAttribute(string title) : Attribute
{
    /// <summary>Titel, im Connector dessen Name; höchstens 30 Zeichen.</summary>
    public string Title { get; } = title;

    /// <summary>Beschreibung der API.</summary>
    public string? Description { get; set; }
}
