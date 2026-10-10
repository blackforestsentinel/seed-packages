using System.Reflection;

namespace Bfs.Seed.Auth;

/// <summary>
/// Findet Tippfehler in <see cref="RequireCapabilityAttribute"/>: Capabilities, die keine Rolle in
/// <c>auth.roles</c> vergibt, wären nie erfüllbar. <c>UseSeedAuth()</c> prüft die App beim Start;
/// ein Test im Projekt findet dieselben Fehler schon im Build.
/// </summary>
public static class SeedCapabilityCheck
{
    /// <summary>
    /// Liefert je unbekannter Capability eine Meldung der Form <c>Typ.Methode: name</c>, leer,
    /// wenn alle Attribute zur Zuordnung passen.
    /// </summary>
    public static IReadOnlyList<string> FindUnknownCapabilities(SeedCapabilityMap map, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(map);

        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var unknown = new List<string>();

        foreach (var type in assemblies.SelectMany(LoadableTypes))
        {
            Collect(type.FullName ?? type.Name, type);
            foreach (var method in type.GetMethods(Members))
            {
                Collect($"{type.FullName}.{method.Name}", method);
            }
        }

        return unknown;

        void Collect(string where, MemberInfo member)
        {
            foreach (var attribute in member.GetCustomAttributes<RequireCapabilityAttribute>(inherit: false))
            {
                if (!map.Capabilities.Contains(attribute.Capability))
                {
                    unknown.Add($"{where}: {attribute.Capability}");
                }
            }
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }
}
