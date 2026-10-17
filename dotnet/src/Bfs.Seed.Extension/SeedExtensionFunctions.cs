using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Bfs.Seed.Extension;

/// <summary>
/// Update-Endpunkt einer selbst gehosteten Browser-Erweiterung. Das <c>update_url</c> im Manifest und
/// die Intune-Richtlinie (ExtensionInstallForcelist) zeigen auf updates.xml; der Browser fragt dort
/// alle paar Stunden nach einer neuen Version und lädt dann das CRX. Beides ist ohne Anmeldung
/// erreichbar, wie der Browser es verlangt: Das CRX ist signiert, sein Inhalt nicht geheim.
/// Die Functions kommen mit dem Paket in jede App, die es referenziert; ohne
/// <c>AddSeedExtensionUpdates()</c> antworten beide mit 404.
/// </summary>
[AllowAnonymous]
public sealed class SeedExtensionFunctions(IServiceProvider services)
{
    /// <summary><c>GET /api/extension/updates.xml</c>: Update-Manifest; 404, solange nichts veröffentlicht ist.</summary>
    [Function("SeedExtensionUpdates")]
    public async Task<IActionResult> UpdatesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = SeedExtension.UpdatesRoute)] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var releases = services.GetService<ISeedExtensionReleases>();
        var release = releases is null ? null : await releases.GetCurrentAsync(cancellationToken);
        if (release is null)
        {
            return new NotFoundResult();
        }

        request.HttpContext.Response.Headers.CacheControl = "no-cache";
        return new ContentResult
        {
            Content = SeedExtension.UpdateManifest(release, SeedExtension.PackageUrl(request, release.File)),
            ContentType = "text/xml; charset=utf-8",
            StatusCode = StatusCodes.Status200OK,
        };
    }

    /// <summary><c>GET /api/extension/packages/{file}</c>: ein signiertes Paket; die Namen enthalten die Version und ändern sich nie.</summary>
    [Function("SeedExtensionPackage")]
    public async Task<IActionResult> PackageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = SeedExtension.PackagesRoute)] HttpRequest request,
        string file,
        CancellationToken cancellationToken)
    {
        var releases = services.GetService<ISeedExtensionReleases>();
        var stream = releases is null ? null : await releases.OpenPackageAsync(file, cancellationToken);
        if (stream is null)
        {
            return new NotFoundResult();
        }

        request.HttpContext.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return new FileStreamResult(stream, SeedExtension.PackageContentType) { FileDownloadName = file };
    }
}
