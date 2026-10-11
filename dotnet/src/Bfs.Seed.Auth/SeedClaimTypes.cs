namespace Bfs.Seed.Auth;

/// <summary>Claim-Typen, die die Token-Prüfung zusätzlich zu den Claims des Tokens setzt.</summary>
public static class SeedClaimTypes
{
    /// <summary>
    /// Eine Capability der angemeldeten Person oder Anwendung, aufgelöst aus ihren App-Rollen
    /// über die zentrale Zuordnung von Rollen zu Capabilities. Je Capability ein Claim.
    /// </summary>
    public const string Capability = "seed:capability";

    /// <summary>App-Rollen aus dem Token (Claim <c>roles</c>).</summary>
    public const string Role = "roles";

    /// <summary>
    /// Anzeigename einer App-Rolle aus <c>auth.roles.&lt;Rolle&gt;.displayName</c>, je Rolle aus dem
    /// Token, die <c>auth.roles</c> kennt, ein Claim. Zu welcher Rolle er gehört, steht in
    /// <see cref="System.Security.Claims.Claim.Properties"/> unter <see cref="RoleProperty"/>;
    /// <see cref="SeedPrincipalExtensions.GetRoleDetails"/> liest beides zusammen.
    /// </summary>
    public const string RoleDisplayName = "seed:role-display-name";

    /// <summary>Schlüssel in den Properties eines <see cref="RoleDisplayName"/>-Claims: der Wert der Rolle.</summary>
    public const string RoleProperty = "seed:role";
}
