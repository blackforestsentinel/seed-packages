using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Bfs.Seed.OpenApi.TestApp;

/// <summary>Anonym über die Klasse.</summary>
[AllowAnonymous]
public sealed class HealthFunction
{
    [Function("Health")]
    [OpenApiOperation("GetHealth", "Status abrufen", Visibility = OpenApiVisibility.Internal)]
    [OpenApiResponse(200, typeof(HealthReport))]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request) => new OkResult();
}

/// <param name="Status">ok oder degraded.</param>
/// <param name="Version">Version der API.</param>
public sealed record HealthReport(string Status, string Version);
