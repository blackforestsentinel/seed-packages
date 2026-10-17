using System.Text.Json;
using Azure;
using Bfs.Seed.Storage;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Extension;

/// <summary>
/// Die zuletzt veröffentlichte Erweiterung, wie die Seed-Pipeline sie in <c>extension/current.json</c>
/// ablegt. Weitere Felder der Datei werden ignoriert.
/// </summary>
/// <param name="Id">Erweiterungs-ID, folgt aus dem Signaturschlüssel.</param>
/// <param name="Version">Version aus manifest.json, etwa 1.1.0.2190.</param>
/// <param name="File">Name des CRX im Container, etwa sentinel-link-1.1.0.2190.crx.</param>
/// <param name="Sha256">SHA-256 des CRX, hexadezimal; der Browser prüft damit den Download.</param>
public sealed record SeedExtensionRelease(string Id, string Version, string File, string Sha256);

/// <summary>Veröffentlichte Pakete der Erweiterung.</summary>
public interface ISeedExtensionReleases
{
    /// <summary>Die aktuelle Veröffentlichung; <c>null</c>, solange noch keine veröffentlicht ist.</summary>
    Task<SeedExtensionRelease?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Das CRX mit diesem Namen; <c>null</c>, wenn der Name ungültig ist oder es fehlt.</summary>
    Task<Stream?> OpenPackageAsync(string file, CancellationToken cancellationToken);
}

/// <summary>
/// Pakete im Container <c>extension</c>. Die Pipeline lädt zuerst <c>&lt;name&gt;-&lt;version&gt;.crx</c>
/// hoch und danach <c>current.json</c>; ältere CRX bleiben liegen, damit ein laufender Download
/// nicht ins Leere geht. Jede Instanz hält current.json kurz im Speicher; Browser fragen ohnehin nur
/// alle paar Stunden.
/// </summary>
internal sealed class SeedBlobExtensionReleases(
    SeedStorageClients clients,
    IOptions<SeedExtensionOptions> options,
    TimeProvider time) : ISeedExtensionReleases
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object gate = new();
    private (DateTimeOffset LoadedAt, SeedExtensionRelease? Release)? cached;

    public async Task<SeedExtensionRelease?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (cached is { } entry && time.GetUtcNow() - entry.LoadedAt < options.Value.CacheDuration)
            {
                return entry.Release;
            }
        }

        var container = await clients.GetContainerAsync(options.Value.ContainerName, cancellationToken);
        SeedExtensionRelease? release;
        try
        {
            var content = await container.GetBlobClient(SeedExtension.CurrentFile).DownloadContentAsync(cancellationToken);
            release = content.Value.Content.ToObjectFromJson<SeedExtensionRelease>(Json);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            release = null;
        }

        lock (gate)
        {
            cached = (time.GetUtcNow(), release);
        }

        return release;
    }

    public async Task<Stream?> OpenPackageAsync(string file, CancellationToken cancellationToken)
    {
        if (!SeedExtension.IsPackageFile(options.Value.PackageName!, file))
        {
            return null;
        }

        var container = await clients.GetContainerAsync(options.Value.ContainerName, cancellationToken);
        try
        {
            var download = await container.GetBlobClient(file).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return download.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }
}
