using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Beantwortet MCP-Anfragen und liefert die Metadaten. Jede Anfrage bekommt einen eigenen
/// Transport und Server, die genau so lange leben wie sie: Eine Function hält keinen Zustand
/// zwischen Aufrufen, und Flex Consumption verteilt Aufrufe auf beliebig viele Instanzen. Ohne
/// Sitzung funktionieren <c>tools/list</c> und <c>tools/call</c> auch ohne vorheriges <c>initialize</c>.
/// </summary>
internal sealed class SeedMcpEndpoint(
    SeedMcpToolCatalog catalog,
    IOptions<SeedMcpOptions> options,
    IOptions<SeedAuthOptions> authOptions,
    ILoggerFactory loggerFactory)
{
    // Entra stellt nur mit offline_access ein Refresh-Token aus. Nicht jeder Client fordert es
    // von sich aus an; ohne müsste man sich nach einer Stunde neu verbinden.
    private const string OfflineAccess = "offline_access";

    private static readonly string Version = ReadVersion();

    private readonly ILogger logger = loggerFactory.CreateLogger<SeedMcpEndpoint>();

    public async Task<IActionResult> HandleAsync(HttpRequest request, IServiceProvider requestServices)
    {
        var http = request.HttpContext;
        if (!HttpMethods.IsPost(request.Method))
        {
            // Zustandslos: kein Ereignisstrom per GET, keine Sitzung, die DELETE beenden könnte (Spezifikation: 405).
            http.Response.Headers.Allow = "POST";
            return new StatusCodeResult(StatusCodes.Status405MethodNotAllowed);
        }

        // Im Normalfall lässt die Token-Prüfung aus Bfs.Seed.Auth eine Anfrage ohne Token gar nicht
        // bis hierher. Fehlt sie, bleibt der Endpunkt trotzdem zu.
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            http.Response.Headers.WWWAuthenticate =
                $"Bearer resource_metadata=\"{SeedMcpResource.BaseUrl(request)}{SeedMcp.ResourceMetadataPath}\"";
            return new UnauthorizedResult();
        }

        var cancellationToken = http.RequestAborted;
        JsonRpcMessage? message;
        try
        {
            message = await JsonSerializer.DeserializeAsync<JsonRpcMessage>(request.Body, McpJsonUtilities.DefaultOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // Kein JSON-RPC, also auch kein Gegenüber, das eine JSON-RPC-Fehlermeldung lesen würde.
            message = null;
        }

        if (message is null)
        {
            return new BadRequestObjectResult("Der Rumpf ist keine JSON-RPC-Nachricht.");
        }

        // So sehen Werkzeuge und Filter die angemeldete Person (RequestContext.User).
        message.Context = new JsonRpcMessageContext { User = user };

        await using var transport = new StreamableHttpServerTransport(loggerFactory) { Stateless = true };
        await using var server = McpServer.Create(transport, CreateServerOptions(user), loggerFactory, requestServices);

        // Der Server liest aus dem Transport, solange es ihn gibt. Die Anfrage endet mit der
        // geschriebenen Antwort, nicht mit dieser Schleife; deshalb wird sie nicht abgewartet.
        _ = server.RunAsync(cancellationToken);

        var answered = await transport.HandlePostRequestAsync(
            message,
            http.Response.Body,
            _ =>
            {
                // Vor dem ersten Byte, danach stehen die Header fest.
                http.Response.StatusCode = StatusCodes.Status200OK;
                http.Response.ContentType = "text/event-stream";
                return ValueTask.CompletedTask;
            },
            cancellationToken);

        if (!answered)
        {
            // Eine Benachrichtigung wie notifications/initialized hat keine Antwort.
            http.Response.StatusCode = StatusCodes.Status202Accepted;
        }

        return new Microsoft.AspNetCore.Mvc.EmptyResult();
    }

    public IActionResult ResourceMetadata(HttpRequest request)
    {
        var auth = authOptions.Value;
        if (string.IsNullOrWhiteSpace(auth.TenantId) || string.IsNullOrWhiteSpace(auth.ClientId))
        {
            // Ohne Tenant gibt es keinen Aussteller, auf den die Metadaten verweisen könnten.
            return new NotFoundResult();
        }

        var resource = SeedMcpResource.For(request, options.Value, auth);
        var metadata = new SeedMcpResourceMetadata(
            Resource: resource.Url,
            AuthorizationServers: [$"{auth.Instance.TrimEnd('/')}/{auth.TenantId}/v2.0"],
            ScopesSupported: [resource.Scope, OfflineAccess],
            BearerMethodsSupported: ["header"],
            ResourceName: options.Value.ServerName);

        // Selbst serialisiert: Der MVC-Formatter würde die Namen in camelCase umschreiben.
        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(metadata),
        };
    }

    private McpServerOptions CreateServerOptions(ClaimsPrincipal user)
    {
        // Nur die erlaubten Werkzeuge: tools/list zeigt nichts anderes, und tools/call findet nichts anderes.
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in catalog.AllowedFor(user))
        {
            tools.Add(tool);
        }

        // Die Sammlung des SDK listet in Hash-Reihenfolge. Clients sollen die Werkzeuge stabil in
        // der Reihenfolge der Registrierung sehen, damit sich die Liste nur mit dem Code ändert.
        var filters = new McpServerFilters();
        filters.Request.ListToolsFilters.Add(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            result.Tools = [.. result.Tools.OrderBy(t => catalog.IndexOf(t.Name))];
            return result;
        });

        var settings = options.Value;
        return new McpServerOptions
        {
            Filters = filters,
            ServerInfo = new Implementation { Name = settings.ServerName ?? "seed", Version = Version },
            ServerInstructions = settings.Instructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            ToolCollection = tools,
            // Eine Anfrage ist eine Function-Ausführung, deren Scope genügt. Ein zweiter Scope
            // legte Dienste wie einen DbContext doppelt an.
            ScopeRequests = false,
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [] }),
                // Nur für Werkzeuge, die nicht in der Liste stehen.
                CallToolHandler = (request, _) => ValueTask.FromResult(Refuse(request.Params?.Name, user)),
            },
        };
    }

    private CallToolResult Refuse(string? name, ClaimsPrincipal user)
    {
        var tool = catalog.Find(name)
            ?? throw new McpProtocolException($"Unbekanntes Werkzeug: '{name}'.", McpErrorCode.InvalidParams);

        var missing = tool.MissingCapabilities(user);
        logger.LogWarning("MCP-Werkzeug {Tool} abgelehnt, es fehlen die Capabilities {Capabilities}.", tool.Name, missing);
        return SeedMcpTool.Refusal(tool.Name, missing);
    }

    private static string ReadVersion()
    {
        // "1.2.3+<commit>": Die Build-Metadaten gehen den Client nichts an.
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var release = version?.Split('+')[0].Trim();
        return string.IsNullOrEmpty(release) ? "0.0.0" : release;
    }
}
