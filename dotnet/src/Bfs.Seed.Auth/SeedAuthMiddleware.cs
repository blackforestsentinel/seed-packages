using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Bfs.Seed.Auth;

/// <summary>
/// Lässt HTTP-Aufrufe nur mit gültigem Bearer-Token durch. Functions mit
/// <see cref="AllowAnonymousAttribute"/> an Methode oder Klasse sind ausgenommen.
/// </summary>
internal sealed class SeedAuthMiddleware(SeedTokenValidator validator, ILogger<SeedAuthMiddleware> logger)
    : IFunctionsWorkerMiddleware
{
    private static readonly ConcurrentDictionary<string, bool> AnonymousFunctions = new();

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!IsHttpTrigger(context) || AnonymousFunctions.GetOrAdd(context.FunctionDefinition.EntryPoint, AllowsAnonymous))
        {
            await next(context);
            return;
        }

        var http = context.GetHttpContext()
            ?? throw new InvalidOperationException("UseSeedAuth() braucht die ASP.NET-Core-Integration: builder.ConfigureFunctionsWebApplication().");

        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(http, "missing_token");
            return;
        }

        var result = await validator.ValidateAsync(header["Bearer ".Length..].Trim(), http.RequestAborted);
        if (!result.IsValid)
        {
            logger.LogWarning("Token für {Function} abgelehnt: {Reason}", context.FunctionDefinition.Name, result.Error);
            await RejectAsync(http, "invalid_token");
            return;
        }

        http.User = result.Principal!;
        await next(context);
    }

    private static bool IsHttpTrigger(FunctionContext context) =>
        context.FunctionDefinition.InputBindings.Values.Any(b => string.Equals(b.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase));

    internal static bool AllowsAnonymous(string entryPoint)
    {
        var separator = entryPoint.LastIndexOf('.');
        var typeName = entryPoint[..separator];
        var methodName = entryPoint[(separator + 1)..];

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName, throwOnError: false))
            .FirstOrDefault(t => t is not null);
        var method = type?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == methodName);

        return method?.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any() == true
            || type?.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any() == true;
    }

    private static Task RejectAsync(HttpContext http, string error)
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers.WWWAuthenticate = $"Bearer error=\"{error}\"";
        return Task.CompletedTask;
    }
}
