namespace Bfs.Seed.Mcp;

/// <summary>
/// Einstellungen des MCP-Servers aus dem Abschnitt <c>Mcp</c>. Das Terraform-Modul sso setzt
/// <c>Mcp__Resource</c>, wenn das Projekt eine eigene Domain für die Function hat.
/// </summary>
public sealed class SeedMcpOptions
{
    /// <summary>Name des Konfigurationsabschnitts.</summary>
    public const string SectionName = "Mcp";

    /// <summary>
    /// Kanonische Adresse des Endpunkts auf der eigenen Domain, z. B.
    /// <c>https://mcp.example.org/api/mcp</c>. Sie steht zugleich als Application ID URI an der
    /// API-Registrierung: Claude sendet die Adresse als <c>resource</c> (RFC 8707), und Entra stellt
    /// nur dann ein Token aus. Ohne Angabe gilt die Adresse, unter der die Anfrage ankommt.
    /// </summary>
    public string? Resource { get; set; }

    /// <summary>Name, unter dem sich der Server meldet. Standard: <c>Seed:Project</c>.</summary>
    public string? ServerName { get; set; }

    /// <summary>Hinweise an das Modell, wie es die Werkzeuge nutzen soll (<c>instructions</c> in <c>initialize</c>).</summary>
    public string? Instructions { get; set; }
}
