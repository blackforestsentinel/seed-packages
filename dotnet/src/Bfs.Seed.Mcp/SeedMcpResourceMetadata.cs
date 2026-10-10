using System.Text.Json.Serialization;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Metadaten nach RFC 9728. Die Feldnamen stehen ausgeschrieben, weil Clients exakt diese
/// snake_case-Namen erwarten; eine Namensregel des Serializers darf sie nicht ändern.
/// </summary>
/// <param name="Resource">Kanonische Adresse des MCP-Endpunkts.</param>
/// <param name="AuthorizationServers">Aussteller der Tokens, hier der Entra-Tenant (v2.0).</param>
/// <param name="ScopesSupported">Scopes, die ein Client anfordern soll.</param>
/// <param name="BearerMethodsSupported">Nur der Header <c>Authorization</c>.</param>
/// <param name="ResourceName">Name zur Anzeige im Client.</param>
internal sealed record SeedMcpResourceMetadata(
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string> AuthorizationServers,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("bearer_methods_supported")] IReadOnlyList<string> BearerMethodsSupported,
    [property: JsonPropertyName("resource_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResourceName);
