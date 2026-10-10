using System.Text.RegularExpressions;

namespace Bfs.Seed.Storage;

/// <summary>Regeln von Table Storage für Tabellennamen und Schlüssel.</summary>
internal static partial class SeedTableKeys
{
    public static bool IsValidTableName(string? name) => name is not null && TableNamePattern().IsMatch(name);

    /// <summary>
    /// Table Storage lehnt <c>/ \ # ?</c> und Steuerzeichen in Schlüsseln mit einem wenig
    /// sprechenden 400 ab; hier fällt das mit dem Namen des Felds auf.
    /// </summary>
    public static void Validate(string? key, string paramName)
    {
        ArgumentNullException.ThrowIfNull(key, paramName);
        if (key.Length > 1024 || InvalidKeyCharacters().IsMatch(key))
        {
            throw new ArgumentException($"{paramName}: höchstens 1024 Zeichen, ohne / \\ # ? und Steuerzeichen.", paramName);
        }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]{2,62}$")]
    private static partial Regex TableNamePattern();

    [GeneratedRegex(@"[/\\#?\u0000-\u001F\u007F-\u009F]")]
    private static partial Regex InvalidKeyCharacters();
}
