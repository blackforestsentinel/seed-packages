using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Auth;

/// <summary>
/// Lässt HTTP-Aufrufe nur mit gültigem Bearer-Token durch und setzt die Attribute an der Function
/// durch: <see cref="RequireScopeAttribute"/>, <see cref="RequireAnyRoleAttribute"/> und
/// <see cref="RequireCapabilityAttribute"/>. Functions mit <see cref="AllowAnonymousAttribute"/> an
/// Methode oder Klasse sind ausgenommen.
/// </summary>
internal sealed class SeedAuthMiddleware(
    SeedTokenValidator validator,
    IOptions<SeedAuthOptions> options,
    SeedCapabilityMap capabilities,
    ILogger<SeedAuthMiddleware> logger,
    IEnumerable<ISeedAuthChallengeScope>? challengeScopes = null)
    : IFunctionsWorkerMiddleware
{
    private static readonly ConcurrentDictionary<(string EntryPoint, string Name), FunctionRequirements> Requirements = new();

    private readonly Lazy<ClaimsPrincipal> localPrincipal = new(() => SeedLocalPrincipal.Create(options.Value, capabilities));

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!IsHttpTrigger(context))
        {
            await next(context);
            return;
        }

        var definition = context.FunctionDefinition;
        var requirements = Requirements.GetOrAdd((definition.EntryPoint, definition.Name), key => Inspect(key.EntryPoint, key.Name));
        if (requirements.AllowAnonymous)
        {
            await next(context);
            return;
        }

        var http = context.GetHttpContext()
            ?? throw new InvalidOperationException("UseSeedAuth() braucht die ASP.NET-Core-Integration: builder.ConfigureFunctionsWebApplication().");

        if (options.Value.Mode == SeedAuthMode.Local)
        {
            // Lokal ohne Token: Der Entwicklungsnutzer gilt für jede delegierte Berechtigung.
            http.User = localPrincipal.Value;
        }
        else
        {
            var header = http.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                // Ohne Token kein Fehlercode (RFC 6750, Abschnitt 3.1): Der Client weiß nur noch
                // nicht, dass er sich anmelden muss.
                Reject(http, StatusCodes.Status401Unauthorized, error: null);
                return;
            }

            var scopes = requirements.Scopes ?? [options.Value.RequiredScope];
            var result = await validator.ValidateAsync(header["Bearer ".Length..].Trim(), scopes, http.RequestAborted);
            if (!result.IsValid)
            {
                if (result.Error == SeedTokenValidationResult.ClientNotAllowed)
                {
                    // Die Client-ID ist kein Geheimnis und zeigt, welche Anwendung in der Liste fehlt.
                    logger.LogWarning("Token für {Function} abgelehnt: {Reason}, Client {ClientId} steht nicht in Auth:AllowedClientIds.",
                        context.FunctionDefinition.Name, result.Error, result.ClientId ?? "(ohne azp/appid)");
                }
                else
                {
                    logger.LogWarning("Token für {Function} abgelehnt: {Reason}", context.FunctionDefinition.Name, result.Error);
                }

                // Gültiges Token ohne passende Berechtigung: 403 nach RFC 6750, sonst 401, auch für
                // eine nicht zugelassene Anwendung.
                if (result.Error == SeedTokenValidationResult.MissingScope)
                {
                    Reject(http, StatusCodes.Status403Forbidden, "insufficient_scope", scopes);
                }
                else if (result.IsInsufficient)
                {
                    Reject(http, StatusCodes.Status403Forbidden, "insufficient_scope");
                }
                else
                {
                    Reject(http, StatusCodes.Status401Unauthorized, "invalid_token");
                }

                return;
            }

            http.User = result.Principal!;
        }

        // Jede Rolle im Token ist eine App-Rolle dieser API: Entra stellt nur deren Rollen aus.
        if (requirements.RequireAnyRole && http.User.GetRoles().Count == 0)
        {
            logger.LogInformation("{Function} verlangt eine App-Rolle, {User} hat keine.",
                context.FunctionDefinition.Name, http.User.FindFirst("oid")?.Value);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var missing = requirements.Capabilities.FirstOrDefault(c => !http.User.HasCapability(c));
        if (missing is not null)
        {
            logger.LogInformation("{Function} verlangt die Capability {Capability}, die {User} nicht hat.",
                context.FunctionDefinition.Name, missing, http.User.FindFirst("oid")?.Value);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    private static bool IsHttpTrigger(FunctionContext context) =>
        context.FunctionDefinition.InputBindings.Values.Any(b => string.Equals(b.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase));

    internal static bool AllowsAnonymous(string entryPoint, string functionName) => Inspect(entryPoint, functionName).AllowAnonymous;

    /// <summary>
    /// Liest die Attribute an Methode und Klasse; die Methode geht bei Scopes vor. Die Methode ist die
    /// mit <c>[Function]</c> unter dem Namen der Function: Der Entry Point nennt nur den Methodennamen,
    /// und den können zwei Functions einer Klasse teilen.
    /// </summary>
    internal static FunctionRequirements Inspect(string entryPoint, string functionName)
    {
        var separator = entryPoint.LastIndexOf('.');
        var typeName = separator > 0 ? entryPoint[..separator] : entryPoint;
        var methodName = entryPoint[(separator + 1)..];

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName, throwOnError: false))
            .FirstOrDefault(t => t is not null);
        var methods = type?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == methodName && m.GetCustomAttribute<FunctionAttribute>()?.Name == functionName)
            .ToList() ?? [];

        // Ohne eindeutige Methode keine Anforderungen raten: leere Anforderungen ließen jedes gültige Token durch.
        if (methods.Count != 1)
        {
            throw new InvalidOperationException(
                $"UseSeedAuth(): Für die Function {functionName} ({entryPoint}) gibt es {(methods.Count == 0 ? "keine" : "mehr als eine")} " +
                $"öffentliche Methode {methodName} mit [Function(\"{functionName}\")]. Die Anforderungen der Function lassen sich nicht bestimmen.");
        }

        var method = methods[0];
        var attributes = method.GetCustomAttributes(inherit: true)
            .Concat(type!.GetCustomAttributes(inherit: true))
            .ToList();

        // [AllowWithoutRole] ändert nichts: Ohne Rolle durchzulassen ist das Verhalten ohne Attribut.
        return new FunctionRequirements(
            AllowAnonymous: attributes.OfType<IAllowAnonymous>().Any(),
            Scopes: attributes.OfType<RequireScopeAttribute>().FirstOrDefault()?.Scopes,
            Capabilities: attributes.OfType<RequireCapabilityAttribute>().Select(a => a.Capability).Distinct(StringComparer.Ordinal).ToList(),
            RequireAnyRole: attributes.OfType<RequireAnyRoleAttribute>().Any());
    }

    private void Reject(HttpContext http, int status, string? error, IReadOnlyList<string>? scopes = null)
    {
        http.Response.StatusCode = status;
        var request = http.Request;
        var parameters = new List<string>();
        if (error is not null)
        {
            parameters.Add($"error=\"{error}\"");
        }

        // Voll qualifizierte Scopes (etwa vom MCP-Endpunkt) gehen den Kurznamen aus [RequireScope] vor.
        var scope = challengeScopes?.Select(c => c.For(request)).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
            ?? (scopes is null ? null : string.Join(' ', scopes));
        if (scope is not null)
        {
            parameters.Add($"scope=\"{scope}\"");
        }

        if (!string.IsNullOrWhiteSpace(options.Value.ResourceMetadataPath))
        {
            // Hinter dem Functions-Host kommt die Anfrage intern per HTTP an; nach außen gilt HTTPS.
            var scheme = request.Host.Host is "localhost" or "127.0.0.1" ? request.Scheme : "https";
            parameters.Add($"resource_metadata=\"{scheme}://{request.Host}{options.Value.ResourceMetadataPath}\"");
        }

        http.Response.Headers.WWWAuthenticate = parameters.Count == 0 ? "Bearer" : $"Bearer {string.Join(", ", parameters)}";
    }

    /// <summary>Was eine Function verlangt, einmal je Function per Reflection ermittelt.</summary>
    internal sealed record FunctionRequirements(bool AllowAnonymous, IReadOnlyList<string>? Scopes, IReadOnlyList<string> Capabilities, bool RequireAnyRole);
}
