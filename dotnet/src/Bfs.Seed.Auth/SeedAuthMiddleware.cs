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
/// durch: <see cref="RequireScopeAttribute"/> und <see cref="RequireCapabilityAttribute"/>.
/// Functions mit <see cref="AllowAnonymousAttribute"/> an Methode oder Klasse sind ausgenommen.
/// </summary>
internal sealed class SeedAuthMiddleware(
    SeedTokenValidator validator,
    IOptions<SeedAuthOptions> options,
    SeedCapabilityMap capabilities,
    ILogger<SeedAuthMiddleware> logger)
    : IFunctionsWorkerMiddleware
{
    private static readonly ConcurrentDictionary<string, FunctionRequirements> Requirements = new();

    private readonly Lazy<ClaimsPrincipal> localPrincipal = new(() => SeedLocalPrincipal.Create(options.Value, capabilities));

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!IsHttpTrigger(context))
        {
            await next(context);
            return;
        }

        var requirements = Requirements.GetOrAdd(context.FunctionDefinition.EntryPoint, Inspect);
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
                Reject(http, StatusCodes.Status401Unauthorized, "missing_token");
                return;
            }

            var scopes = requirements.Scopes ?? [options.Value.RequiredScope];
            var result = await validator.ValidateAsync(header["Bearer ".Length..].Trim(), scopes, http.RequestAborted);
            if (!result.IsValid)
            {
                logger.LogWarning("Token für {Function} abgelehnt: {Reason}", context.FunctionDefinition.Name, result.Error);
                // Gültiges Token ohne passende Berechtigung: 403 nach RFC 6750, sonst 401.
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

    internal static bool AllowsAnonymous(string entryPoint) => Inspect(entryPoint).AllowAnonymous;

    /// <summary>Liest die Attribute an Methode und Klasse; die Methode geht bei Scopes vor.</summary>
    internal static FunctionRequirements Inspect(string entryPoint)
    {
        var separator = entryPoint.LastIndexOf('.');
        var typeName = entryPoint[..separator];
        var methodName = entryPoint[(separator + 1)..];

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName, throwOnError: false))
            .FirstOrDefault(t => t is not null);
        var method = type?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == methodName);

        var attributes = (method?.GetCustomAttributes(inherit: true) ?? [])
            .Concat(type?.GetCustomAttributes(inherit: true) ?? [])
            .ToList();

        return new FunctionRequirements(
            AllowAnonymous: attributes.OfType<IAllowAnonymous>().Any(),
            Scopes: attributes.OfType<RequireScopeAttribute>().FirstOrDefault()?.Scopes,
            Capabilities: attributes.OfType<RequireCapabilityAttribute>().Select(a => a.Capability).Distinct(StringComparer.Ordinal).ToList());
    }

    private void Reject(HttpContext http, int status, string error, IReadOnlyList<string>? scopes = null)
    {
        http.Response.StatusCode = status;
        var challenge = $"Bearer error=\"{error}\"";
        if (scopes is not null)
        {
            challenge += $", scope=\"{string.Join(' ', scopes)}\"";
        }

        if (!string.IsNullOrWhiteSpace(options.Value.ResourceMetadataPath))
        {
            // Hinter dem Functions-Host kommt die Anfrage intern per HTTP an; nach außen gilt HTTPS.
            var request = http.Request;
            var scheme = request.Host.Host is "localhost" or "127.0.0.1" ? request.Scheme : "https";
            challenge += $", resource_metadata=\"{scheme}://{request.Host}{options.Value.ResourceMetadataPath}\"";
        }

        http.Response.Headers.WWWAuthenticate = challenge;
    }

    /// <summary>Was eine Function verlangt, einmal je Function per Reflection ermittelt.</summary>
    internal sealed record FunctionRequirements(bool AllowAnonymous, IReadOnlyList<string>? Scopes, IReadOnlyList<string> Capabilities);
}
