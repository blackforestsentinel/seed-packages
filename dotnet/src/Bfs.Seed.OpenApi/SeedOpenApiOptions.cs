using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Bfs.Seed.OpenApi;

/// <summary>Einstellungen für <see cref="SeedOpenApiDocument"/>; die Standards passen zu Seed-Apps.</summary>
public sealed class SeedOpenApiOptions
{
    /// <summary>
    /// Wie die App JSON schreibt: Web-Standards von ASP.NET Core (camelCase), Zahlen als Zahlen.
    /// </summary>
    public static JsonSerializerOptions DefaultSerializerOptions { get; } = CreateDefaultSerializerOptions();

    /// <summary>Präfix der Routen aus host.json (<c>extensions.http.routePrefix</c>), Standard <c>api</c>.</summary>
    public string RoutePrefix { get; set; } = "api";

    /// <summary>JSON-Einstellungen für die Schemas.</summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = DefaultSerializerOptions;

    /// <summary>
    /// Anmeldung per Entra ID (OAuth 2.0, Authorization Code) für alle Operationen ohne
    /// <c>[AllowAnonymous]</c>. Die URLs sind mandantenneutral; Client-ID und Scope je Umgebung
    /// setzt der Custom Connector selbst (apiProperties.json).
    /// </summary>
    public bool OAuth2 { get; set; } = true;

    /// <summary>Autorisierungs-URL im securityDefinition.</summary>
    public string AuthorizationUrl { get; set; } = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";

    /// <summary>Token-URL im securityDefinition.</summary>
    public string TokenUrl { get; set; } = "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    private static JsonSerializerOptions CreateDefaultSerializerOptions()
    {
        // Web lässt Zahlen auch als Strings lesen; das machte im Schema aus integer [string, integer].
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }
}
