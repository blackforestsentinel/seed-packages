using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bfs.Seed.Auth;

/// <summary>Wer eine App-Rolle tragen darf; entspricht <c>allowedMemberTypes</c> in Entra ID.</summary>
[Flags]
public enum SeedMemberTypes
{
    /// <summary>Niemand.</summary>
    None = 0,

    /// <summary>Personen und Gruppen, Token mit <c>scp</c>.</summary>
    User = 1,

    /// <summary>Anwendungen und Dienste, Token ohne angemeldete Person.</summary>
    Application = 2,
}

/// <summary>Eine App-Rolle aus <c>auth.roles</c> in project.yaml.</summary>
/// <param name="Name">Wert der Rolle im <c>roles</c>-Claim.</param>
/// <param name="Description">Beschreibung, erscheint in Entra ID.</param>
/// <param name="MemberTypes">Wer die Rolle tragen darf.</param>
/// <param name="Capabilities">Capabilities, die aus der Rolle folgen.</param>
public sealed record SeedRoleDefinition(
    string Name,
    string Description,
    SeedMemberTypes MemberTypes,
    IReadOnlySet<string> Capabilities);

/// <summary>
/// Zentrale Zuordnung von App-Rollen zu Capabilities aus <c>auth.roles</c> in project.yaml.
/// Terraform legt aus derselben Datei die App-Rollen in Entra ID an; die API löst daraus die
/// Capabilities auf, das Frontend sieht nur das Ergebnis.
/// </summary>
public sealed class SeedCapabilityMap
{
    private SeedCapabilityMap(IReadOnlyDictionary<string, SeedRoleDefinition> roles, string? source)
    {
        Roles = roles;
        Source = source;
        Capabilities = roles.Values.SelectMany(r => r.Capabilities).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Zuordnung ohne Rollen: Tokens ergeben keine Capabilities.</summary>
    public static SeedCapabilityMap Empty { get; } = new(new Dictionary<string, SeedRoleDefinition>(), null);

    /// <summary>Rollen nach ihrem Wert im <c>roles</c>-Claim.</summary>
    public IReadOnlyDictionary<string, SeedRoleDefinition> Roles { get; }

    /// <summary>Alle Capabilities, die irgendeine Rolle vergibt.</summary>
    public IReadOnlySet<string> Capabilities { get; }

    /// <summary>Datei, aus der die Zuordnung stammt; <c>null</c>, wenn sie fehlt.</summary>
    public string? Source { get; }

    /// <summary>
    /// Liest <c>auth.roles</c> aus project.yaml. Fehlt die Datei, ist die Zuordnung leer; so laufen
    /// Projekte ohne Rollen unverändert. Ein fehlerhafter Abschnitt bricht mit einer Meldung ab.
    /// </summary>
    public static SeedCapabilityMap Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path), path) : Empty;

    /// <summary>Liest <c>auth.roles</c> aus dem Inhalt einer project.yaml.</summary>
    public static SeedCapabilityMap Parse(string yaml, string source = "project.yaml")
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException e)
        {
            throw new InvalidOperationException($"{source}: kein gültiges YAML ({e.Message}).", e);
        }

        var roles = new Dictionary<string, SeedRoleDefinition>(StringComparer.Ordinal);
        var rolesNode = stream.Documents.Count == 0 ? null : Child(Child(stream.Documents[0].RootNode, "auth"), "roles");
        if (rolesNode is null)
        {
            return new SeedCapabilityMap(roles, source);
        }

        if (rolesNode is not YamlMappingNode mapping)
        {
            throw Invalid(source, "auth.roles", "muss eine Zuordnung Rolle → Einstellungen sein");
        }

        foreach (var (key, value) in mapping.Children)
        {
            var name = Scalar(key) ?? throw Invalid(source, "auth.roles", "enthält eine Rolle ohne Namen");
            var path = $"auth.roles.{name}";
            if (name.Any(char.IsWhiteSpace))
            {
                throw Invalid(source, path, "Rollennamen dürfen keine Leerzeichen enthalten");
            }

            var capabilities = Strings(Child(value, "capabilities"), source, $"{path}.capabilities");
            if (capabilities.FirstOrDefault(c => c.Length == 0 || c.Any(char.IsWhiteSpace)) is { } invalid)
            {
                throw Invalid(source, $"{path}.capabilities", $"„{invalid}“ ist kein gültiger Name (leer oder mit Leerzeichen)");
            }

            var memberTypes = Child(value, "memberTypes") is { } typesNode
                ? Strings(typesNode, source, $"{path}.memberTypes").Aggregate(SeedMemberTypes.None, (all, t) => all | MemberType(t, source, path))
                : SeedMemberTypes.User;
            if (memberTypes == SeedMemberTypes.None)
            {
                throw Invalid(source, $"{path}.memberTypes", "braucht User, Application oder beide");
            }

            roles[name] = new SeedRoleDefinition(
                name,
                Scalar(Child(value, "description")) ?? name,
                memberTypes,
                capabilities.ToHashSet(StringComparer.Ordinal));
        }

        return new SeedCapabilityMap(roles, source);
    }

    /// <summary>
    /// Capabilities, die aus den Rollen folgen. Es zählen nur Rollen, die für den Mitgliedstyp
    /// vorgesehen sind; unbekannte Rollen ergeben nichts.
    /// </summary>
    public IReadOnlySet<string> Resolve(IEnumerable<string> roles, SeedMemberTypes memberType) =>
        roles
            .Select(r => Roles.GetValueOrDefault(r))
            .Where(r => r is not null && (r.MemberTypes & memberType) != 0)
            .SelectMany(r => r!.Capabilities)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Ob eine der Rollen für Anwendungen ohne angemeldete Person vorgesehen ist.</summary>
    public bool AllowsApplication(IEnumerable<string> roles) =>
        roles.Any(r => Roles.TryGetValue(r, out var role) && role.MemberTypes.HasFlag(SeedMemberTypes.Application));

    private static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child : null;

    private static string? Scalar(YamlNode? node) =>
        node is YamlScalarNode { Value: { } value } && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static List<string> Strings(YamlNode? node, string source, string path) => node switch
    {
        null => [],
        YamlSequenceNode sequence => sequence.Children
            .Select(c => c is YamlScalarNode s ? s.Value?.Trim() ?? string.Empty : throw Invalid(source, path, "darf nur Texte enthalten"))
            .ToList(),
        YamlScalarNode { Value: null or "" } => [],
        _ => throw Invalid(source, path, "muss eine Liste sein, z. B. [invoices.read, invoices.write]"),
    };

    private static SeedMemberTypes MemberType(string value, string source, string path) => value switch
    {
        "User" => SeedMemberTypes.User,
        "Application" => SeedMemberTypes.Application,
        _ => throw Invalid(source, $"{path}.memberTypes", $"„{value}“ ist unbekannt, erlaubt sind User und Application"),
    };

    private static InvalidOperationException Invalid(string source, string path, string message) =>
        new($"{source}: {path} {message}.");
}
