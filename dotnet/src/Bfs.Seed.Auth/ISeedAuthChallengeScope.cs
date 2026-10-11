using Microsoft.AspNetCore.Http;

namespace Bfs.Seed.Auth;

/// <summary>
/// Nennt für eine Anfrage die Scopes, die eine 401- oder 403-Antwort der Token-Prüfung im Header
/// <c>WWW-Authenticate</c> als <c>scope</c> angibt, voll qualifiziert (RFC 6750, Abschnitt 3). Die
/// MCP-Autorisierung empfiehlt ihn, und Clients wie Claude fordern genau diese Scopes bei Entra an.
/// Bfs.Seed.Mcp registriert eine Umsetzung für den MCP-Endpunkt. Ohne passende Umsetzung nennt eine
/// 401 keinen Scope und eine 403 die Kurznamen aus <see cref="RequireScopeAttribute"/>.
/// </summary>
public interface ISeedAuthChallengeScope
{
    /// <summary>Scopes, durch Leerzeichen getrennt, oder <c>null</c>, wenn die Anfrage nicht dazugehört.</summary>
    string? For(HttpRequest request);
}
