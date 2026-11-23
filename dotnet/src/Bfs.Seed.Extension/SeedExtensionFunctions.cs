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
/// <remarks>
/// Schließt der Aufrufer die Verbindung, bevor die Antwort feststeht, enden beide Functions ohne
/// Fehler mit Status 499. Der Browser fragt später ohnehin wieder;
/// als Fehler gezählt würde so ein Abbruch nur Meldungen auslösen, die niemanden betreffen.
/// </remarks>
[AllowAnonymous]
public sealed class SeedExtensionFunctions(IServiceProvider services)
{
    /// <summary>
    /// 499 „Client Closed Request“ (nicht standardisiert, aus nginx): Der Aufrufer hat die Verbindung
    /// geschlossen, die Antwort liest niemand mehr. So bleiben solche Aufrufe in der Telemetrie
    /// erkennbar, ohne als Fehler der App zu gelten.
    /// </summary>
    internal const int StatusClientClosedRequest = 499;

    /// <summary><c>GET /api/extension/updates.xml</c>: Update-Manifest; 404, solange nichts veröffentlicht ist.</summary>
    [Function("SeedExtensionUpdates")]
    public async Task<IActionResult> UpdatesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = SeedExtension.UpdatesRoute)] HttpRequest request,
        CancellationToken cancellationToken)
    {
        SeedExtensionRelease? release;
        try
        {
            var releases = services.GetService<ISeedExtensionReleases>();
            release = releases is null ? null : await releases.GetCurrentAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (CallerLeft(request, cancellationToken))
        {
            return ClientClosedRequest();
        }

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
        Stream? stream;
        try
        {
            var releases = services.GetService<ISeedExtensionReleases>();
            stream = releases is null ? null : await releases.OpenPackageAsync(file, cancellationToken);
        }
        catch (OperationCanceledException) when (CallerLeft(request, cancellationToken))
        {
            return ClientClosedRequest();
        }

        if (stream is null)
        {
            return new NotFoundResult();
        }

        // Bricht der Download erst beim Schreiben ab, fängt das ASP.NET Core selbst ab (FileStreamResult).
        request.HttpContext.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return new FileStreamResult(stream, SeedExtension.PackageContentType) { FileDownloadName = file };
    }

    /// <summary>
    /// Der Abbruch kam vom Aufrufer: Das Token der Invocation oder <see cref="HttpContext.RequestAborted"/>
    /// ist ausgelöst. Andere Abbrüche, etwa ein Timeout des Storage-Clients, bleiben Fehler.
    /// </summary>
    private static bool CallerLeft(HttpRequest request, CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested || request.HttpContext.RequestAborted.IsCancellationRequested;

    private static StatusCodeResult ClientClosedRequest() => new(StatusClientClosedRequest);
}
