namespace Bfs.Seed.Auth;

/// <summary>
/// Verlangt eine Capability für eine HTTP-Function (an Methode oder Klasse) oder ein Werkzeug.
/// Mehrere Attribute müssen alle erfüllt sein. Ohne Anmeldung antwortet die API mit 401,
/// angemeldet ohne die Capability mit 403.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireCapabilityAttribute(string capability) : Attribute
{
    /// <summary>Name der Capability, z. B. <c>invoices.write</c>.</summary>
    public string Capability { get; } = capability;
}
