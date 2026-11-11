using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Auth.Tests;

public class SeedAuthMiddlewareTests
{
    private static readonly SeedCapabilityMap Map = SeedCapabilityMap.Parse(SeedCapabilityMapTests.ProjectYaml);

    private readonly TestTokens tokens = new();

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var (status, called, http) = await InvokeAsync(nameof(CapabilityFunctions.Me), token: null);

        Assert.Equal((401, false), (status, called));
        // Ohne Token kein Fehlercode (RFC 6750, Abschnitt 3.1).
        Assert.Equal("Bearer", http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task InvalidToken_Returns401()
    {
        var (status, called, _) = await InvokeAsync(nameof(CapabilityFunctions.Me), token: tokens.CreateToken(audience: "api://fremd"));

        Assert.Equal((401, false), (status, called));
    }

    [Fact]
    public async Task ClientNotInAllowedClientIds_Returns401LikeInvalidToken()
    {
        tokens.AuthOptions.AllowedClientIds = [TestTokens.SpaClientId];
        const string other = "77777777-7777-7777-7777-777777777777";

        var spa = await InvokeAsync(nameof(CapabilityFunctions.Me), tokens.CreateToken());
        var foreign = await InvokeAsync(nameof(CapabilityFunctions.Me), tokens.CreateToken(clientId: other));
        var service = await InvokeAsync(nameof(CapabilityFunctions.Me), tokens.CreateToken(scope: null, roles: ["Sync"], clientId: other));

        Assert.Equal((200, true), (spa.Status, spa.Called));
        Assert.Equal((401, false), (foreign.Status, foreign.Called));
        Assert.Equal("Bearer error=\"invalid_token\"", foreign.Http.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal((200, true), (service.Status, service.Called));
    }

    [Fact]
    public async Task ValidToken_SetsUserWithCapabilities()
    {
        var (status, called, http) = await InvokeAsync(nameof(CapabilityFunctions.Update), tokens.CreateToken(roles: ["Admin"]));

        Assert.Equal((200, true), (status, called));
        Assert.True(http.User.HasCapability("invoices.write"));
    }

    [Theory]
    [InlineData(nameof(CapabilityFunctions.List), "")]
    [InlineData(nameof(CapabilityFunctions.Update), "Reader")]
    public async Task MissingCapability_Returns403(string function, string roles)
    {
        var token = tokens.CreateToken(roles: roles.Split(',', StringSplitOptions.RemoveEmptyEntries));

        var (status, called, _) = await InvokeAsync(function, token);

        Assert.Equal((403, false), (status, called));
    }

    [Fact]
    public async Task AllowAnonymous_SkipsTokenCheck()
    {
        var (status, called, _) = await InvokeAsync(nameof(CapabilityFunctions.Health), token: null);

        Assert.Equal((200, true), (status, called));
    }

    [Fact]
    public async Task RequireScope_OnClass_AcceptsOneOfTheScopes()
    {
        var mcp = await InvokeAsync(nameof(ScopedFunctions.Tools), tokens.CreateToken(scope: "mcp_access", roles: ["Reader"]), typeof(ScopedFunctions));
        var other = await InvokeAsync(nameof(ScopedFunctions.Tools), tokens.CreateToken(scope: "other", roles: ["Reader"]), typeof(ScopedFunctions));

        Assert.Equal(200, mcp.Status);
        Assert.Equal(403, other.Status);
        Assert.Equal(
            "Bearer error=\"insufficient_scope\", scope=\"mcp_access access_as_user\"",
            other.Http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task RequireScope_OnMethod_ReplacesClassAttribute()
    {
        var result = await InvokeAsync(nameof(ScopedFunctions.Special), tokens.CreateToken(scope: "mcp_access", roles: ["Admin"]), typeof(ScopedFunctions));

        Assert.Equal(403, result.Status);
    }

    [Fact]
    public async Task AppOnlyToken_UsesApplicationRoles()
    {
        var sync = await InvokeAsync(nameof(CapabilityFunctions.Me), tokens.CreateToken(scope: null, roles: ["Sync"]));
        var reader = await InvokeAsync(nameof(CapabilityFunctions.Me), tokens.CreateToken(scope: null, roles: ["Reader"]));
        var admin = await InvokeAsync(nameof(CapabilityFunctions.Update), tokens.CreateToken(scope: null, roles: ["Admin"]));

        Assert.Equal((200, true), (sync.Status, sync.Called));
        Assert.True(sync.Http.User.IsApplication());
        Assert.Equal((403, false), (reader.Status, reader.Called));
        Assert.Equal(200, admin.Status);
    }

    [Fact]
    public async Task ResourceMetadataPath_IsNamedInChallenge()
    {
        tokens.AuthOptions.ResourceMetadataPath = "/.well-known/oauth-protected-resource";

        var (_, _, http) = await InvokeAsync(nameof(CapabilityFunctions.Me), token: null);

        Assert.Equal(
            "Bearer resource_metadata=\"https://api.example.org/.well-known/oauth-protected-resource\"",
            http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task ChallengeScope_IsNamedAndBeatsShortScopes()
    {
        var challenge = new FixedChallengeScope("https://mcp.example.org/api/mcp/mcp_access offline_access");

        var missing = await InvokeAsync(nameof(CapabilityFunctions.Me), token: null, challenge: challenge);
        var other = await InvokeAsync(nameof(ScopedFunctions.Tools), tokens.CreateToken(scope: "other", roles: ["Reader"]), typeof(ScopedFunctions), challenge);

        Assert.Equal(
            "Bearer scope=\"https://mcp.example.org/api/mcp/mcp_access offline_access\"",
            missing.Http.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal(
            "Bearer error=\"insufficient_scope\", scope=\"https://mcp.example.org/api/mcp/mcp_access offline_access\"",
            other.Http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task ChallengeScope_WithoutMatch_KeepsShortScopes()
    {
        var other = await InvokeAsync(nameof(ScopedFunctions.Tools), tokens.CreateToken(scope: "other", roles: ["Reader"]), typeof(ScopedFunctions), new FixedChallengeScope(null));

        Assert.Equal(
            "Bearer error=\"insufficient_scope\", scope=\"mcp_access access_as_user\"",
            other.Http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task LocalMode_UsesDevelopmentUserWithoutToken()
    {
        tokens.AuthOptions.Mode = SeedAuthMode.Local;
        tokens.AuthOptions.LocalUser.Roles = ["Reader"];

        var list = await InvokeAsync(nameof(CapabilityFunctions.List), token: null);
        var update = await InvokeAsync(nameof(CapabilityFunctions.Update), token: null);

        Assert.Equal((200, true), (list.Status, list.Called));
        Assert.Equal("Lokale Entwicklung", list.Http.User.Identity?.Name);
        Assert.Equal(["Reader"], list.Http.User.GetRoles());
        Assert.Equal(403, update.Status);
    }

    [Fact]
    public async Task RoleWithoutCapabilities_IsReadableAsAudience()
    {
        var map = SeedCapabilityMap.Parse(SeedCapabilityMapTests.AudienceYaml);

        var (status, called, http) = await InvokeAsync(nameof(CapabilityFunctions.Me), tokens.CreateToken(roles: ["Team.Vertrieb"]), map: map);
        var info = SeedUserInfo.From(http.User);

        Assert.Equal((200, true), (status, called));
        Assert.True(http.User.IsInRole("Team.Vertrieb"));
        Assert.False(http.User.IsInRole("Team.Einkauf"));
        Assert.Equal(["Team.Vertrieb"], http.User.GetRoles());
        Assert.Empty(http.User.GetCapabilities());
        Assert.Equal(["Team.Vertrieb"], info.Roles);
        Assert.Equal([new SeedRoleDetail("Team.Vertrieb", "Team Vertrieb")], info.RoleDetails);
        Assert.Empty(info.Capabilities);
    }

    [Fact]
    public async Task WithoutAttribute_ValidTokenWithoutRole_IsAccepted()
    {
        // Das Verhalten, das SEEDAUTH001 sichtbar macht: Ohne Attribut genügt jedes gültige Token.
        var (status, called, http) = await InvokeAsync(nameof(CapabilityFunctions.Me), tokens.CreateToken());

        Assert.Equal((200, true), (status, called));
        Assert.Empty(http.User.GetRoles());
    }

    [Fact]
    public async Task RequireAnyRole_WithoutRole_Returns403()
    {
        var (status, called, _) = await InvokeAsync(nameof(RoleFunctions.Orders), tokens.CreateToken(), typeof(RoleFunctions));

        Assert.Equal((403, false), (status, called));
    }

    [Fact]
    public async Task RequireAnyRole_WithoutToken_Returns401()
    {
        var (status, called, _) = await InvokeAsync(nameof(RoleFunctions.Orders), token: null, typeof(RoleFunctions));

        Assert.Equal((401, false), (status, called));
    }

    [Theory]
    [InlineData("Reader")]
    [InlineData("Unbekannt")]
    public async Task RequireAnyRole_AnyRoleOfTheApi_IsEnough(string role)
    {
        // Auch eine Rolle, die auth.roles nicht kennt: Entra stellt nur Rollen dieser API aus.
        var (status, called, _) = await InvokeAsync(nameof(RoleFunctions.Orders), tokens.CreateToken(roles: [role]), typeof(RoleFunctions));

        Assert.Equal((200, true), (status, called));
    }

    [Fact]
    public async Task RequireAnyRole_RoleWithoutCapabilities_IsEnough()
    {
        var map = SeedCapabilityMap.Parse(SeedCapabilityMapTests.AudienceYaml);

        var (status, called, _) = await InvokeAsync(nameof(RoleFunctions.Orders), tokens.CreateToken(roles: ["Team.Vertrieb"]), typeof(RoleFunctions), map: map);

        Assert.Equal((200, true), (status, called));
    }

    [Fact]
    public async Task RequireAnyRole_AppOnlyToken_HasItsApplicationRole()
    {
        var (status, called, _) = await InvokeAsync(nameof(RoleFunctions.Orders), tokens.CreateToken(scope: null, roles: ["Sync"]), typeof(RoleFunctions));

        Assert.Equal((200, true), (status, called));
    }

    [Fact]
    public async Task RequireAnyRole_LocalModeWithoutRoles_Returns403()
    {
        tokens.AuthOptions.Mode = SeedAuthMode.Local;

        var (status, called, _) = await InvokeAsync(nameof(RoleFunctions.Orders), token: null, typeof(RoleFunctions));

        Assert.Equal((403, false), (status, called));
    }

    [Fact]
    public async Task AllowWithoutRole_AcceptsTokenWithoutRole_ButNotWithoutToken()
    {
        var withToken = await InvokeAsync(nameof(RoleFunctions.Me), tokens.CreateToken(), typeof(RoleFunctions));
        var withoutToken = await InvokeAsync(nameof(RoleFunctions.Me), token: null, typeof(RoleFunctions));

        Assert.Equal((200, true), (withToken.Status, withToken.Called));
        Assert.Equal((401, false), (withoutToken.Status, withoutToken.Called));
    }

    [Fact]
    public async Task AllowWithoutRole_DoesNotLiftRequireAnyRoleOfTheClass()
    {
        var (status, called, _) = await InvokeAsync(nameof(RoleClassFunctions.Run), tokens.CreateToken(), typeof(RoleClassFunctions));

        Assert.Equal((403, false), (status, called));
    }

    [Fact]
    public void Inspect_CombinesClassAndMethodAttributes()
    {
        var requirements = SeedAuthMiddleware.Inspect($"{typeof(ScopedFunctions).FullName}.{nameof(ScopedFunctions.Special)}", nameof(ScopedFunctions.Special));

        Assert.False(requirements.AllowAnonymous);
        Assert.Equal(["special_scope"], requirements.Scopes);
        Assert.Equal(["invoices.read", "invoices.write"], requirements.Capabilities.Order());
        Assert.False(requirements.RequireAnyRole);
        Assert.True(SeedAuthMiddleware.Inspect($"{typeof(RoleClassFunctions).FullName}.{nameof(RoleClassFunctions.Run)}", nameof(RoleClassFunctions.Run)).RequireAnyRole);
    }

    [Fact]
    public async Task SameMethodName_EachFunctionGetsItsOwnAttributes()
    {
        // Drei Functions mit dem Entry Point SameNameFunctions.Run: Jede bekommt die Attribute ihrer Methode.
        var reader = await InvokeAsync(nameof(SameNameFunctions.Run), tokens.CreateToken(roles: ["Reader"]), typeof(SameNameFunctions), function: "SameNameInvoices");
        var admin = await InvokeAsync(nameof(SameNameFunctions.Run), tokens.CreateToken(roles: ["Admin"]), typeof(SameNameFunctions), function: "SameNameInvoices");
        var status = await InvokeAsync(nameof(SameNameFunctions.Run), token: null, typeof(SameNameFunctions), function: "SameNameStatus");
        var queueWithoutToken = await InvokeAsync(nameof(SameNameFunctions.Run), token: null, typeof(SameNameFunctions), function: "SameNameQueue");
        var queue = await InvokeAsync(nameof(SameNameFunctions.Run), tokens.CreateToken(), typeof(SameNameFunctions), function: "SameNameQueue");

        Assert.Equal((403, false), (reader.Status, reader.Called));
        Assert.Equal((200, true), (admin.Status, admin.Called));
        Assert.Equal((200, true), (status.Status, status.Called));
        Assert.Equal((401, false), (queueWithoutToken.Status, queueWithoutToken.Called));
        Assert.Equal((200, true), (queue.Status, queue.Called));
    }

    [Fact]
    public void SameMethodName_InspectUsesTheFunctionName()
    {
        var entryPoint = $"{typeof(SameNameFunctions).FullName}.{nameof(SameNameFunctions.Run)}";

        Assert.Equal(["invoices.write"], SeedAuthMiddleware.Inspect(entryPoint, "SameNameInvoices").Capabilities);
        Assert.True(SeedAuthMiddleware.AllowsAnonymous(entryPoint, "SameNameStatus"));
        Assert.False(SeedAuthMiddleware.AllowsAnonymous(entryPoint, "SameNameInvoices"));
        Assert.Empty(SeedAuthMiddleware.Inspect(entryPoint, "SameNameQueue").Capabilities);
    }

    [Theory]
    [InlineData(typeof(SameNameFunctions), "Run", "SameNameUnknown")]
    [InlineData(typeof(UnmarkedFunctions), "Run", "Run")]
    [InlineData(typeof(CapabilityFunctions), "Missing", "Missing")]
    public async Task WithoutMatchingFunctionAttribute_Fails(Type type, string method, string function)
    {
        // Ohne eindeutige Methode gibt es keine Anforderungen, die man durchsetzen könnte: Fehler statt Durchlass.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(method, tokens.CreateToken(roles: ["Admin"]), type, function: function));

        Assert.Contains($"Function {function} ({type.FullName}.{method})", error.Message);
        Assert.Contains($"[Function(\"{function}\")]", error.Message);
    }

    [Fact]
    public async Task UnknownType_Fails()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync("Run", tokens.CreateToken(), entryPoint: "Bfs.Seed.Auth.Tests.Missing.Run"));

        Assert.Contains("Function Run (Bfs.Seed.Auth.Tests.Missing.Run)", error.Message);
    }

    private async Task<(int Status, bool Called, HttpContext Http)> InvokeAsync(
        string method,
        string? token,
        Type? type = null,
        ISeedAuthChallengeScope? challenge = null,
        SeedCapabilityMap? map = null,
        string? function = null,
        string? entryPoint = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("api.example.org");
        if (token is not null)
        {
            http.Request.Headers.Authorization = $"Bearer {token}";
        }

        var middleware = new SeedAuthMiddleware(
            tokens.CreateValidator(map ?? Map),
            Options.Create(tokens.AuthOptions),
            map ?? Map,
            NullLogger<SeedAuthMiddleware>.Instance,
            challenge is null ? null : [challenge]);

        var called = false;
        // Ohne eigenen Namen heißt die Function wie ihre Methode, wie bei [Function(nameof(...))].
        entryPoint ??= $"{(type ?? typeof(CapabilityFunctions)).FullName}.{method}";
        await middleware.Invoke(new TestFunctionContext(entryPoint, function ?? method, http), _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        return (http.Response.StatusCode, called, http);
    }

    private sealed class FixedChallengeScope(string? scope) : ISeedAuthChallengeScope
    {
        public string? For(HttpRequest request) => scope;
    }

    private sealed class TestFunctionContext(string entryPoint, string name, HttpContext http) : FunctionContext
    {
        public override string InvocationId => "test";
        public override string FunctionId => entryPoint;
        public override TraceContext TraceContext => null!;
        public override BindingContext BindingContext => null!;
        public override RetryContext RetryContext => null!;
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition { get; } = new TestFunctionDefinition(entryPoint, name);

        // Hier sucht GetHttpContext() aus der ASP.NET-Core-Integration den HttpContext.
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object> { ["HttpRequestContext"] = http };
        public override IInvocationFeatures Features => null!;
    }

    private sealed class TestFunctionDefinition(string entryPoint, string name) : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => [];
        public override string PathToAssembly => typeof(TestFunctionDefinition).Assembly.Location;
        public override string EntryPoint => entryPoint;
        public override string Id => entryPoint;
        public override string Name => name;

        public override IImmutableDictionary<string, BindingMetadata> InputBindings { get; } =
            ImmutableDictionary<string, BindingMetadata>.Empty.Add("request", new TestBinding());

        public override IImmutableDictionary<string, BindingMetadata> OutputBindings => ImmutableDictionary<string, BindingMetadata>.Empty;
    }

    private sealed class TestBinding : BindingMetadata
    {
        public override string Name => "request";
        public override string Type => "httpTrigger";
        public override BindingDirection Direction => BindingDirection.In;
    }
}
