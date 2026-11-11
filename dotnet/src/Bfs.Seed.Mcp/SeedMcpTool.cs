using System.Security.Claims;
using Bfs.Seed.Auth;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Bfs.Seed.Mcp;

/// <summary>
/// Ein Werkzeug mit den Anforderungen aus seinen Attributen (an Methode und Klasse):
/// <see cref="RequireCapabilityAttribute"/> und <see cref="RequireAnyRoleAttribute"/>. Vor dem
/// Aufruf prüft es diese und die Argumente. <see cref="AllowWithoutRoleAttribute"/> verlangt nichts,
/// legt aber ausdrücklich fest, dass jedes angemeldete Konto das Werkzeug verwenden darf.
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
        RequiresRole = inner.Metadata.OfType<RequireAnyRoleAttribute>().Any();
        HasAuthorization = RequiredCapabilities.Count > 0 || RequiresRole || inner.Metadata.OfType<AllowWithoutRoleAttribute>().Any();
    }

    /// <summary>
    /// Ob Methode oder Klasse festlegen, wer das Werkzeug verwenden darf: <see cref="RequireCapabilityAttribute"/>,
    /// <see cref="RequireAnyRoleAttribute"/> oder <see cref="AllowWithoutRoleAttribute"/>.
    /// </summary>
    public bool HasAuthorization { get; }

    /// <summary>Capabilities, die alle vorhanden sein müssen.</summary>
    public IReadOnlyList<string> RequiredCapabilities { get; }

    /// <summary>Ob das Werkzeug irgendeine App-Rolle verlangt (<see cref="RequireAnyRoleAttribute"/>).</summary>
    public bool RequiresRole { get; }

    public string Name => ProtocolTool.Name;

    public IReadOnlyList<string> MissingCapabilities(ClaimsPrincipal? user) =>
        RequiredCapabilities.Where(c => user?.HasCapability(c) != true).ToArray();

    /// <summary>Ob das Werkzeug eine App-Rolle verlangt, die Person aber keine hat.</summary>
    public bool LacksRole(ClaimsPrincipal? user) => RequiresRole && (user is null || user.GetRoles().Count == 0);

    public bool IsAllowedFor(ClaimsPrincipal? user) => !LacksRole(user) && MissingCapabilities(user).Count == 0;

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        // Die Werkzeugliste je Anfrage enthält ohnehin nur erlaubte Werkzeuge. Die Prüfung hier
        // hält auch dann, wenn ein Werkzeug auf anderem Weg in einen Server gelangt.
        var missing = MissingCapabilities(request.User);
        if (missing.Count > 0 || LacksRole(request.User))
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

    /// <summary>
    /// Antwort, wenn die angemeldete Person das Werkzeug nicht verwenden darf: mit den fehlenden
    /// Capabilities, ohne sie mit der fehlenden App-Rolle.
    /// </summary>
    public static CallToolResult Refusal(string name, IReadOnlyList<string> missing) => Error(missing.Count > 0
        ? $"Das Werkzeug „{name}“ ist für dich nicht freigegeben. Es fehlt die Berechtigung " +
            $"{string.Join(", ", missing.Select(c => $"„{c}“"))}; sie kommt über eine App-Rolle der Anwendung."
        : $"Das Werkzeug „{name}“ ist für dich nicht freigegeben. Es verlangt eine App-Rolle der Anwendung, und du hast keine.");

    private static CallToolResult Error(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }],
    };
}
