using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.AspNetCore.Http;

namespace Bfs.Seed.Extension;

/// <summary>Feste Namen und das Format des Update-Endpunkts.</summary>
public static class SeedExtension
{
    /// <summary>Container im Storage Account des Projekts (<c>storage.containers</c> in project.yaml).</summary>
    public const string ContainerName = "extension";

    /// <summary>Beschreibt die aktuelle Veröffentlichung; die Pipeline schreibt sie nach dem CRX.</summary>
    public const string CurrentFile = "current.json";

    /// <summary>Route des Update-Manifests, auf die <c>update_url</c> im Manifest und die Intune-Richtlinie zeigen.</summary>
    public const string UpdatesRoute = "extension/updates.xml";

    /// <summary>Route der Pakete.</summary>
    public const string PackagesRoute = "extension/packages/{file}";

    /// <summary>Content-Type eines CRX.</summary>
    public const string PackageContentType = "application/x-chrome-extension";

    private static readonly Regex PackageNamePattern = new("^[a-z0-9][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Gültiger Präfix für Paketnamen: klein, Ziffern, Bindestriche, wie <c>extension.name</c> in der Pipeline.</summary>
    public static bool IsValidPackageName(string? name) => name is not null && PackageNamePattern.IsMatch(name);

    /// <summary>
    /// Prüft einen angefragten Dateinamen: nur <c>&lt;name&gt;-&lt;version&gt;.crx</c>, wie die Pipeline
    /// sie vergibt. Alles andere, auch <c>current.json</c> und Pfade mit <c>../</c>, gibt es nicht.
    /// </summary>
    public static bool IsPackageFile(string packageName, string? file)
    {
        if (file is null || !IsValidPackageName(packageName) || !file.StartsWith(packageName + "-", StringComparison.Ordinal))
        {
            return false;
        }

        return PackageVersionPattern.IsMatch(file[(packageName.Length + 1)..]);
    }

    private static readonly Regex PackageVersionPattern = new(@"^[0-9]{1,5}(\.[0-9]{1,5}){0,3}\.crx$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Update-Manifest im gupdate-Format (Protokoll 2.0), wie Edge und Chrome es für Erweiterungen außerhalb des Stores lesen.</summary>
    public static string UpdateManifest(SeedExtensionRelease release, string packageUrl)
    {
        ArgumentNullException.ThrowIfNull(release);

        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = false };
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            const string ns = "http://www.google.com/update2/response";
            writer.WriteStartElement("gupdate", ns);
            writer.WriteAttributeString("protocol", "2.0");
            writer.WriteStartElement("app", ns);
            writer.WriteAttributeString("appid", release.Id);
            writer.WriteStartElement("updatecheck", ns);
            writer.WriteAttributeString("codebase", packageUrl);
            writer.WriteAttributeString("version", release.Version);
            writer.WriteAttributeString("hash_sha256", release.Sha256.ToLowerInvariant());
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Absolute Adresse eines Pakets unter demselben Host wie updates.xml. Hinter dem Frontend von
    /// Azure kommt die Anfrage intern als http an; nach außen gilt immer https, außer lokal.
    /// </summary>
    public static string PackageUrl(HttpRequest request, string file)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = request.Host;
        var scheme = host.Host is "localhost" or "127.0.0.1" ? request.Scheme : "https";
        return $"{scheme}://{host}/api/extension/packages/{Uri.EscapeDataString(file)}";
    }
}
