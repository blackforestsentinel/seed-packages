namespace Bfs.Seed.Functions.Core;

/// <summary>
/// Projektkennung aus der Konfiguration. Das Terraform-Modul core setzt dafür die
/// App-Settings <c>Seed__Project</c> und <c>Seed__Environment</c>.
/// </summary>
public sealed class SeedOptions
{
    /// <summary>Name des Konfigurationsabschnitts.</summary>
    public const string SectionName = "Seed";

    /// <summary>Projektname aus project.yaml.</summary>
    public string Project { get; set; } = "local";

    /// <summary>Umgebung, z. B. dev oder prod.</summary>
    public string Environment { get; set; } = "local";
}
