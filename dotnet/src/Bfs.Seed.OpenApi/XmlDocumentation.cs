using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Bfs.Seed.OpenApi;

/// <summary>
/// Beschreibungen aus der XML-Doku neben einer Assembly (<c>GenerateDocumentationFile</c>):
/// <c>summary</c> von Eigenschaften und Feldern, <c>param</c> von Records und Klassen mit
/// primärem Konstruktor. Fehlt die Datei, gibt es keine Beschreibungen, aber keinen Fehler.
/// </summary>
internal sealed partial class XmlDocumentation
{
    private readonly Dictionary<Assembly, Dictionary<string, XElement>> files = [];

    /// <summary><c>summary</c> einer Eigenschaft oder eines Felds.</summary>
    public string? Summary(MemberInfo member)
    {
        var prefix = member switch
        {
            PropertyInfo => "P:",
            FieldInfo => "F:",
            _ => null,
        };
        if (prefix is null || member.DeclaringType is not { } type || TypeId(type) is not { } typeId)
        {
            return null;
        }

        return Text(Members(type.Assembly).GetValueOrDefault($"{prefix}{typeId}.{member.Name}")?.Element("summary"));
    }

    /// <summary><c>param</c> eines Konstruktorparameters, am Typ (Record, primärer Konstruktor) oder am Konstruktor.</summary>
    public string? Parameter(ParameterInfo parameter)
    {
        if (parameter.Member is not ConstructorInfo { DeclaringType: { } type } || TypeId(type) is not { } typeId)
        {
            return null;
        }

        var members = Members(type.Assembly);
        var candidates = new[] { members.GetValueOrDefault($"T:{typeId}") }
            .Concat(members.Where(m => m.Key.StartsWith($"M:{typeId}.#ctor", StringComparison.Ordinal)).Select(m => m.Value));
        return candidates
            .Select(m => m?.Elements("param").FirstOrDefault(p => (string?)p.Attribute("name") == parameter.Name))
            .Select(Text)
            .FirstOrDefault(t => t is not null);
    }

    private Dictionary<string, XElement> Members(Assembly assembly)
    {
        if (!files.TryGetValue(assembly, out var members))
        {
            files[assembly] = members = Load(assembly);
        }

        return members;
    }

    private static Dictionary<string, XElement> Load(Assembly assembly)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(assembly.Location))
        {
            return result;
        }

        var path = Path.ChangeExtension(assembly.Location, ".xml");
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            foreach (var member in XDocument.Load(path).Root?.Element("members")?.Elements("member") ?? [])
            {
                if ((string?)member.Attribute("name") is { } name)
                {
                    result.TryAdd(name, member);
                }
            }
        }
        catch (XmlException)
        {
            // Kaputte Doku-Datei: dann eben ohne Beschreibungen.
        }

        return result;
    }

    /// <summary>ID wie in der XML-Doku: Namespace und Typ, verschachtelte Typen mit Punkt, Generics als <c>Typ`1</c>.</summary>
    private static string? TypeId(Type type) =>
        (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName?.Replace('+', '.');

    /// <summary>Fließtext ohne Tags: <c>see</c> wird zum Namen, <c>c</c> und <c>para</c> zu ihrem Text.</summary>
    private static string? Text(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        Append(element, builder);
        var text = Whitespace().Replace(builder.ToString(), " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static void Append(XElement element, StringBuilder builder)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement { Name.LocalName: "see" or "seealso", IsEmpty: true } see:
                    builder.Append((string?)see.Attribute("langword") ?? ShortName((string?)see.Attribute("cref")) ?? (string?)see.Attribute("href"));
                    break;
                case XElement { Name.LocalName: "paramref" or "typeparamref" } reference:
                    builder.Append((string?)reference.Attribute("name"));
                    break;
                case XElement { Name.LocalName: "para" or "br" } block:
                    builder.Append(' ');
                    Append(block, builder);
                    builder.Append(' ');
                    break;
                case XElement other:
                    Append(other, builder);
                    break;
            }
        }
    }

    /// <summary><c>T:Ns.Typ</c> wird <c>Typ</c>, <c>M:Ns.Typ.Methode(System.String)</c> wird <c>Methode</c>.</summary>
    private static string? ShortName(string? cref)
    {
        if (cref is null)
        {
            return null;
        }

        var name = cref[(cref.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var parenthesis = name.IndexOf('(', StringComparison.Ordinal);
        if (parenthesis >= 0)
        {
            name = name[..parenthesis];
        }

        name = name[(name.LastIndexOf('.') + 1)..];
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick >= 0 ? name[..tick] : name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
