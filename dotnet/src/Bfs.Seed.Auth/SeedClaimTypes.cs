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
}
