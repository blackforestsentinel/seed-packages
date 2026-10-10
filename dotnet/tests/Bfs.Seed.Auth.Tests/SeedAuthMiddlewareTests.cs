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
        Assert.Equal("Bearer error=\"missing_token\"", http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task InvalidToken_Returns401()
    {
        var (status, called, _) = await InvokeAsync(nameof(CapabilityFunctions.Me), token: tokens.CreateToken(audience: "api://fremd"));

        Assert.Equal((401, false), (status, called));
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
            "Bearer error=\"missing_token\", resource_metadata=\"https://api.example.org/.well-known/oauth-protected-resource\"",
            http.Response.Headers.WWWAuthenticate.ToString());
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
    public void Inspect_CombinesClassAndMethodAttributes()
    {
        var requirements = SeedAuthMiddleware.Inspect($"{typeof(ScopedFunctions).FullName}.{nameof(ScopedFunctions.Special)}");

        Assert.False(requirements.AllowAnonymous);
        Assert.Equal(["special_scope"], requirements.Scopes);
        Assert.Equal(["invoices.read", "invoices.write"], requirements.Capabilities.Order());
    }

    private async Task<(int Status, bool Called, HttpContext Http)> InvokeAsync(string method, string? token, Type? type = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("api.example.org");
        if (token is not null)
        {
            http.Request.Headers.Authorization = $"Bearer {token}";
        }

        var middleware = new SeedAuthMiddleware(
            tokens.CreateValidator(Map),
            Options.Create(tokens.AuthOptions),
            Map,
            NullLogger<SeedAuthMiddleware>.Instance);

        var called = false;
        await middleware.Invoke(new TestFunctionContext($"{(type ?? typeof(CapabilityFunctions)).FullName}.{method}", http), _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        return (http.Response.StatusCode, called, http);
    }

    private sealed class TestFunctionContext(string entryPoint, HttpContext http) : FunctionContext
    {
        public override string InvocationId => "test";
        public override string FunctionId => entryPoint;
        public override TraceContext TraceContext => null!;
        public override BindingContext BindingContext => null!;
        public override RetryContext RetryContext => null!;
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition { get; } = new TestFunctionDefinition(entryPoint);

        // Hier sucht GetHttpContext() aus der ASP.NET-Core-Integration den HttpContext.
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object> { ["HttpRequestContext"] = http };
        public override IInvocationFeatures Features => null!;
    }

    private sealed class TestFunctionDefinition(string entryPoint) : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => [];
        public override string PathToAssembly => typeof(TestFunctionDefinition).Assembly.Location;
        public override string EntryPoint => entryPoint;
        public override string Id => entryPoint;
        public override string Name => entryPoint[(entryPoint.LastIndexOf('.') + 1)..];

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
