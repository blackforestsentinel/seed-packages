using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Alle Werkzeuge der App, einmal aus den registrierten Klassen erzeugt. Das Schema je Werkzeug
/// entsteht per Reflection und wird deshalb nicht bei jeder Anfrage neu gebaut.
/// </summary>
internal sealed class SeedMcpToolCatalog
{
    private readonly Lazy<IReadOnlyList<SeedMcpTool>> tools;

    public SeedMcpToolCatalog(IEnumerable<SeedMcpToolType> types, IServiceProvider services)
    {
        var distinct = types.Select(t => t.Type).Distinct().ToArray();
        tools = new Lazy<IReadOnlyList<SeedMcpTool>>(() => Build(distinct, services));
    }

    /// <summary>Alle Werkzeuge in der Reihenfolge der Registrierung.</summary>
    public IReadOnlyList<SeedMcpTool> All => tools.Value;

    public SeedMcpTool? Find(string? name) => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    public int IndexOf(string name)
    {
        var index = All.ToList().FindIndex(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>Werkzeuge, deren Capabilities die angemeldete Person alle hat.</summary>
    public IEnumerable<SeedMcpTool> AllowedFor(ClaimsPrincipal? user) => All.Where(t => t.IsAllowedFor(user));

    internal static IReadOnlyList<SeedMcpTool> Build(IEnumerable<Type> types, IServiceProvider services)
    {
        var result = new List<SeedMcpTool>();
        foreach (var type in types)
        {
            var methods = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.IsDefined(typeof(McpServerToolAttribute), inherit: false))
                .ToArray();
            if (methods.Length == 0)
            {
                throw new InvalidOperationException($"{type.FullName} hat keine Methode mit [McpServerTool].");
            }

            foreach (var method in methods)
            {
                // Services bestimmt, welche Parameter aus der Dependency Injection kommen statt aus den Argumenten.
                var options = new McpServerToolCreateOptions { Services = services };
                var tool = new SeedMcpTool(method.IsStatic
                    ? McpServerTool.Create(method, target: null, options)
                    : McpServerTool.Create(method, request => ActivatorUtilities.CreateInstance(request.Services!, type), options));

                if (result.Any(t => t.Name == tool.Name))
                {
                    throw new InvalidOperationException($"Das MCP-Werkzeug „{tool.Name}“ ist doppelt definiert.");
                }

                result.Add(tool);
            }
        }

        return result;
    }
}
