namespace Bfs.Seed.Auth;

/// <summary>
/// Gibt eine HTTP-Function (an Methode oder Klasse) oder ein MCP-Werkzeug für jede App-Rolle der API
/// frei, statt eine Capability zu verlangen. Der Aufrufer braucht mindestens eine Rolle im Token,
/// gleich welche; ohne Rolle antwortet die API mit 403. Ohne Anmeldung bleibt es bei 401.
/// </summary>
/// <remarks>
/// App-only-Tokens erfüllen das immer: Die Token-Prüfung nimmt sie nur mit einer Rolle für
/// Anwendungen an. Eine Rolle ohne Capabilities (<c>capabilities: []</c>) zählt auch.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireAnyRoleAttribute : Attribute
{
}
