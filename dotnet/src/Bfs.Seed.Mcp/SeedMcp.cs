namespace Bfs.Seed.Mcp;

/// <summary>Feste Namen des MCP-Endpunkts. Das Terraform-Modul sso verwendet dieselben Werte.</summary>
public static class SeedMcp
{
    /// <summary>
    /// Delegierte Berechtigung, die ein Token für den MCP-Endpunkt tragen muss. Sie gilt nur dort:
    /// Ein Token, das ein MCP-Client bekommt, erreicht die übrige API nicht.
    /// </summary>
    public const string Scope = "mcp_access";

    /// <summary>Route des MCP-Endpunkts unter dem Präfix <c>api</c>, also <c>/api/mcp</c>.</summary>
    public const string Route = "mcp";

    /// <summary>Route der Metadaten nach RFC 9728 unter dem Präfix <c>api</c>.</summary>
    public const string ResourceMetadataRoute = ".well-known/oauth-protected-resource";

    /// <summary>
    /// Öffentlicher Pfad der Metadaten. Jede 401-Antwort nennt ihn als <c>resource_metadata</c>;
    /// darüber finden MCP-Clients Entra als Autorisierungsserver.
    /// </summary>
    public const string ResourceMetadataPath = "/api/" + ResourceMetadataRoute;

    /// <summary>Pfad des MCP-Endpunkts, Teil der kanonischen Adresse.</summary>
    public const string Path = "/api/" + Route;
}
