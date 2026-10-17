namespace Bfs.Seed.Extension;

/// <summary>Einstellungen unter <c>Extension</c> (App-Settings <c>Extension__…</c>); alle optional.</summary>
public sealed class SeedExtensionOptions
{
    /// <summary>Abschnitt in der Konfiguration.</summary>
    public const string SectionName = "Extension";

    /// <summary>
    /// Präfix der Paketnamen (<c>&lt;name&gt;-&lt;version&gt;.crx</c>), wie <c>extension.name</c> in
    /// azure-pipelines.yml. Standard ist <c>Seed:Project</c>, wie in der Pipeline.
    /// </summary>
    public string? PackageName { get; set; }

    /// <summary>Container mit den Paketen; Standard <c>extension</c>.</summary>
    public string ContainerName { get; set; } = SeedExtension.ContainerName;

    /// <summary>Wie lange eine Instanz current.json im Speicher hält; Standard eine Minute.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(1);
}
