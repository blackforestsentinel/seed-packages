using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Bfs.Seed.Mcp;

/// <summary>Werkzeuge und Hinweise des MCP-Servers, aus <see cref="SeedMcpExtensions.AddSeedMcp"/>.</summary>
public sealed class SeedMcpBuilder
{
    internal SeedMcpBuilder(IServiceCollection services) => Services = services;

    /// <summary>Die Dienste der App, etwa für Abhängigkeiten der Werkzeuge.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Nimmt alle Methoden mit <see cref="McpServerToolAttribute"/> aus <typeparamref name="TTools"/>
    /// als Werkzeuge auf. Die Klasse entsteht je Aufruf neu, ihre Abhängigkeiten kommen aus der
    /// Dependency Injection.
    /// </summary>
    public SeedMcpBuilder WithTools<TTools>() where TTools : class => WithTools(typeof(TTools));

    /// <summary>Wie <see cref="WithTools{TTools}"/> für einen Typ zur Laufzeit.</summary>
    public SeedMcpBuilder WithTools(Type toolType)
    {
        ArgumentNullException.ThrowIfNull(toolType);
        Services.AddSingleton(new SeedMcpToolType(toolType));
        return this;
    }

    /// <summary>Nimmt alle Klassen mit <see cref="McpServerToolTypeAttribute"/> aus der Assembly auf.</summary>
    public SeedMcpBuilder WithToolsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetTypes().Where(t => t.IsDefined(typeof(McpServerToolTypeAttribute), inherit: false)))
        {
            WithTools(type);
        }

        return this;
    }

    /// <summary>
    /// Hinweise an das Modell, etwa welches Werkzeug es zuerst aufrufen soll. Überschreibt
    /// <c>Mcp:Instructions</c> aus der Konfiguration.
    /// </summary>
    public SeedMcpBuilder WithInstructions(string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        Services.Configure<SeedMcpOptions>(o => o.Instructions = instructions);
        return this;
    }
}

internal sealed record SeedMcpToolType(Type Type);
