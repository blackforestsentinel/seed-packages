namespace Bfs.Seed.Auth;

/// <summary>Woher die API die angemeldete Person kennt.</summary>
public enum SeedAuthMode
{
    /// <summary>Bearer-Tokens aus Entra ID, Standard.</summary>
    Entra,

    /// <summary>
    /// Fester Entwicklungsnutzer ohne Token, nur lokal. Läuft die App in Azure
    /// (<c>WEBSITE_INSTANCE_ID</c> gesetzt), startet sie in diesem Modus nicht.
    /// </summary>
    Local,
}

/// <summary>Entwicklungsnutzer für <see cref="SeedAuthMode.Local"/>.</summary>
public sealed class SeedLocalUserOptions
{
    /// <summary>Anzeigename, Claim <c>name</c>.</summary>
    public string Name { get; set; } = "Lokale Entwicklung";

    /// <summary>Benutzername, Claim <c>preferred_username</c>.</summary>
    public string Username { get; set; } = "dev@localhost";

    /// <summary>Object-ID, Claim <c>oid</c>.</summary>
    public string ObjectId { get; set; } = "00000000-0000-0000-0000-00000000d001";

    /// <summary>Tenant-ID, Claim <c>tid</c>.</summary>
    public string TenantId { get; set; } = "00000000-0000-0000-0000-00000000d000";

    /// <summary>App-Rollen aus <c>auth.roles</c>, z. B. <c>Auth__LocalUser__Roles__0 = Admin</c>.</summary>
    public List<string> Roles { get; set; } = [];
}

/// <summary>
/// Einstellungen der Token-Prüfung. Das Terraform-Modul sso setzt dafür die App-Settings
/// <c>Auth__TenantId</c>, <c>Auth__ClientId</c> und <c>Auth__Audience</c>, bei einer anderen
/// delegierten Berechtigung als <c>access_as_user</c> zusätzlich <c>Auth__RequiredScope</c>.
/// </summary>
public sealed class SeedAuthOptions
{
    /// <summary>Name des Konfigurationsabschnitts.</summary>
    public const string SectionName = "Auth";

    /// <summary>Entra (Standard) oder Local für die Entwicklung ohne Anmeldung.</summary>
    public SeedAuthMode Mode { get; set; } = SeedAuthMode.Entra;

    /// <summary>Entwicklungsnutzer, nur mit <see cref="SeedAuthMode.Local"/>.</summary>
    public SeedLocalUserOptions LocalUser { get; set; } = new();

    /// <summary>
    /// project.yaml mit der Zuordnung <c>auth.roles</c>, relativ zum Ausgabeordner der App. Das
    /// Template verlinkt die Datei dorthin, damit lokal und in Azure dieselbe Zuordnung gilt.
    /// </summary>
    public string ProjectFile { get; set; } = "project.yaml";

    /// <summary>Tenant-ID von Entra ID.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Client-ID der API-App-Registrierung; bei v2-Tokens der Wert von <c>aud</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Application ID URI der API, z. B. <c>api://&lt;client-id&gt;</c>. Wird zusätzlich als Audience akzeptiert.</summary>
    public string? Audience { get; set; }

    /// <summary>Basis-URL der Anmeldung.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Delegierte Berechtigung, die im <c>scp</c>-Claim stehen muss. <see cref="RequireScopeAttribute"/>
    /// ersetzt sie je Function.
    /// </summary>
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
