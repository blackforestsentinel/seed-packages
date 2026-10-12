using Bfs.Seed.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Bfs.Seed.OpenApi.TestApp;

/// <summary>Antwort mit einem Typ aus einem anderen Paket (Bfs.Seed.Auth).</summary>
public sealed class MeFunction
{
    [Function("Me")]
    [OpenApiOperation("GetMe", "Angemeldete Person abrufen")]
    [OpenApiResponse(200, typeof(SeedUserInfo))]
    [OpenApiResponse(401, Description = "Ohne gültiges Token")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest request) => new OkResult();
}
