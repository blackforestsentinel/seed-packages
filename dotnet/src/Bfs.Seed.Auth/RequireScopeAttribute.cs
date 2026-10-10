namespace Bfs.Seed.Auth;

/// <summary>
/// Ersetzt für eine HTTP-Function (an Methode oder Klasse) die delegierte Berechtigung, die im
/// <c>scp</c>-Claim stehen muss. Eine der genannten genügt. Ohne dieses Attribut gilt
/// <see cref="SeedAuthOptions.RequiredScope"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireScopeAttribute(params string[] scopes) : Attribute
{
    /// <summary>Akzeptierte delegierte Berechtigungen, z. B. <c>mcp_access</c>.</summary>
    public IReadOnlyList<string> Scopes { get; } = scopes;
}
