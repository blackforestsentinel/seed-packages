namespace Bfs.Seed.Auth;

/// <summary>
/// Einstellungen der Token-Prüfung. Das Terraform-Modul sso setzt dafür die App-Settings
/// <c>Auth__TenantId</c>, <c>Auth__ClientId</c> und <c>Auth__Audience</c>.
/// </summary>
public sealed class SeedAuthOptions
{
    /// <summary>Name des Konfigurationsabschnitts.</summary>
    public const string SectionName = "Auth";

    /// <summary>Tenant-ID von Entra ID.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Client-ID der API-App-Registrierung; bei v2-Tokens der Wert von <c>aud</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Application ID URI der API, z. B. <c>api://&lt;client-id&gt;</c>. Wird zusätzlich als Audience akzeptiert.</summary>
    public string? Audience { get; set; }

    /// <summary>Basis-URL der Anmeldung.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>Delegierte Berechtigung, die im <c>scp</c>-Claim stehen muss.</summary>
    public string RequiredScope { get; set; } = "access_as_user";

    /// <summary>
    /// Pfad der Metadaten nach RFC 9728 (Protected Resource Metadata), z. B. für MCP-Clients.
    /// Ist er gesetzt, nennt jede 401-Antwort die volle URL im Header <c>WWW-Authenticate</c>
    /// als <c>resource_metadata</c>.
    /// </summary>
    public string? ResourceMetadataPath { get; set; }

    internal string Authority => $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";

    internal IEnumerable<string> ValidAudiences =>
        new[] { ClientId, Audience ?? $"api://{ClientId}" }.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct();

    internal IEnumerable<string> ValidIssuers =>
        [Authority, $"https://sts.windows.net/{TenantId}/"];
}
