using System.Security.Claims;
using Bfs.Seed.Auth;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Ein Werkzeug mit den Capabilities aus seinen <see cref="RequireCapabilityAttribute"/> (an Methode
/// und Klasse). Vor dem Aufruf prüft es Capabilities und Argumente.
/// </summary>
internal sealed class SeedMcpTool : DelegatingMcpServerTool
{
    public SeedMcpTool(McpServerTool inner)
        : base(inner)
    {
        RequiredCapabilities = inner.Metadata.OfType<RequireCapabilityAttribute>()
            .Select(a => a.Capability)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Capabilities, die alle vorhanden sein müssen.</summary>
    public IReadOnlyList<string> RequiredCapabilities { get; }

    public string Name => ProtocolTool.Name;

    public IReadOnlyList<string> MissingCapabilities(ClaimsPrincipal? user) =>
        RequiredCapabilities.Where(c => user?.HasCapability(c) != true).ToArray();

    public bool IsAllowedFor(ClaimsPrincipal? user) => MissingCapabilities(user).Count == 0;

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        // Die Werkzeugliste je Anfrage enthält ohnehin nur erlaubte Werkzeuge. Die Prüfung hier
        // hält auch dann, wenn ein Werkzeug auf anderem Weg in einen Server gelangt.
        var missing = MissingCapabilities(request.User);
        if (missing.Count > 0)
        {
            return ValueTask.FromResult(Refusal(Name, missing));
        }

        // Das SDK prüft Argumente nicht gegen das Schema; ein unbekanntes Argument fiele still weg
        // und beantwortete womöglich eine breitere Frage als gestellt.
        var errors = SeedMcpArgumentValidator.Validate(ProtocolTool.InputSchema, request.Params?.Arguments);
        if (errors.Count > 0)
        {
            return ValueTask.FromResult(Error(
                $"Die Argumente passen nicht zum Werkzeug „{Name}“: {string.Join(" ", errors)}"));
        }

        return base.InvokeAsync(request, cancellationToken);
    }

    /// <summary>Antwort, wenn die angemeldete Person das Werkzeug nicht verwenden darf.</summary>
    public static CallToolResult Refusal(string name, IReadOnlyList<string> missing) => Error(
        $"Das Werkzeug „{name}“ ist für dich nicht freigegeben. Es fehlt die Berechtigung " +
        $"{string.Join(", ", missing.Select(c => $"„{c}“"))}; sie kommt über eine App-Rolle der Anwendung.");

    private static CallToolResult Error(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }],
    };
}
