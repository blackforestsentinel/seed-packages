namespace Bfs.Seed.Auth;

/// <summary>
/// Gibt eine HTTP-Function (an Methode oder Klasse) oder ein MCP-Werkzeug ausdrücklich für jedes
/// angemeldete Konto frei, auch ohne App-Rolle. Ein gültiges Token verlangt die API weiter.
/// </summary>
/// <remarks>
/// Das Attribut lockert nichts: So verhält sich eine Function ohne Attribut schon heute. Es macht
/// die Entscheidung im Code sichtbar, etwa für <c>GET /api/me</c>, das auch Personen ohne Rolle
/// erreichen müssen, damit das Frontend „Kein Zugriff“ erklären kann. Mit
/// <c>assignmentRequired: false</c> ist das jedes Konto des Tenants. Ein
/// <see cref="RequireCapabilityAttribute"/> oder <see cref="RequireAnyRoleAttribute"/> an Methode oder
/// Klasse gilt trotzdem.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AllowWithoutRoleAttribute : Attribute
{
}
