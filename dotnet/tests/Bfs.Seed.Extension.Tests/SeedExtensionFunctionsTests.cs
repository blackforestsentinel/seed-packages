using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bfs.Seed.Extension.Tests;

public class SeedExtensionFunctionsTests
{
    private static readonly SeedExtensionRelease Release = new("abcdefghijklmnopabcdefghijklmnop", "1.0.0.2210", "kundenportal-1.0.0.2210.crx", "00ff");

    [Fact]
    public async Task WithoutRegistration_BothAnswer404()
    {
        var functions = new SeedExtensionFunctions(new ServiceCollection().BuildServiceProvider());

        Assert.IsType<NotFoundResult>(await functions.UpdatesAsync(Request(), TestContext.Current.CancellationToken));
        Assert.IsType<NotFoundResult>(await functions.PackageAsync(Request(), Release.File, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Updates_ReturnsTheManifest_WithoutCaching()
    {
        var request = Request();
        var functions = Functions(new FakeReleases(Release));

        var result = Assert.IsType<ContentResult>(await functions.UpdatesAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("text/xml; charset=utf-8", result.ContentType);
        Assert.Contains("codebase=\"https://func.example/api/extension/packages/kundenportal-1.0.0.2210.crx\"", result.Content);
        Assert.Equal("no-cache", request.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Updates_Is404_UntilSomethingIsPublished()
    {
        Assert.IsType<NotFoundResult>(await Functions(new FakeReleases(null)).UpdatesAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Package_IsImmutable()
    {
        var request = Request();
        var functions = Functions(new FakeReleases(Release));

        var result = Assert.IsType<FileStreamResult>(await functions.PackageAsync(request, Release.File, TestContext.Current.CancellationToken));

        Assert.Equal(SeedExtension.PackageContentType, result.ContentType);
        Assert.Equal(Release.File, result.FileDownloadName);
        Assert.Equal("public, max-age=31536000, immutable", request.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Package_Is404_ForUnknownFiles()
    {
        Assert.IsType<NotFoundResult>(await Functions(new FakeReleases(Release)).PackageAsync(Request(), "other-1.0.crx", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallerClosesTheConnection_BothEndWith499_WithoutError()
    {
        // Wie im Storage-Client: Das ausgelöste Token der Invocation lässt den Download mit TaskCanceledException enden.
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var functions = Functions(new CancelledReleases());

        var updates = Assert.IsType<StatusCodeResult>(await functions.UpdatesAsync(Request(), aborted.Token));
        var package = Assert.IsType<StatusCodeResult>(await functions.PackageAsync(Request(), Release.File, aborted.Token));

        Assert.Equal(499, updates.StatusCode);
        Assert.Equal(499, package.StatusCode);
    }

    [Fact]
    public async Task RequestAborted_CountsAsCallerLeft()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var request = Request();
        request.HttpContext.RequestAborted = aborted.Token;
        var functions = Functions(new CancelledReleases());

        var updates = Assert.IsType<StatusCodeResult>(await functions.UpdatesAsync(request, TestContext.Current.CancellationToken));
        var package = Assert.IsType<StatusCodeResult>(await functions.PackageAsync(request, Release.File, TestContext.Current.CancellationToken));

        Assert.Equal(499, updates.StatusCode);
        Assert.Equal(499, package.StatusCode);
    }

    [Fact]
    public async Task OtherCancellations_StillFail()
    {
        // Etwa ein Timeout des Storage-Clients, während der Aufrufer noch wartet: ein echter Fehler.
        var functions = Functions(new CancelledReleases());

        await Assert.ThrowsAsync<TaskCanceledException>(() => functions.UpdatesAsync(Request(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TaskCanceledException>(() => functions.PackageAsync(Request(), Release.File, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Register_NeedsTheStorageFeature()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SeedExtensionExtensions.Register(new ServiceCollection(), Configuration(storage: false)));

        Assert.Contains("features.storage", error.Message);
    }

    [Fact]
    public void Register_TakesThePackageNameFromTheProject()
    {
        var services = new ServiceCollection();
        SeedExtensionExtensions.Register(services, Configuration(storage: true, project: "kundenportal"));

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SeedExtensionOptions>>().Value;

        Assert.Equal("kundenportal", options.PackageName);
        Assert.Equal("extension", options.ContainerName);
    }

    [Fact]
    public void Register_RejectsInvalidPackageNames()
    {
        var services = new ServiceCollection();
        SeedExtensionExtensions.Register(services, Configuration(storage: true, project: "Kunden Portal"));

        Assert.Throws<OptionsValidationException>(() => services.BuildServiceProvider().GetRequiredService<IOptions<SeedExtensionOptions>>().Value);
    }

    private static SeedExtensionFunctions Functions(ISeedExtensionReleases releases)
        => new(new ServiceCollection().AddSingleton(releases).BuildServiceProvider());

    private static HttpRequest Request()
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new HostString("func.example");
        return request;
    }

    private static IConfiguration Configuration(bool storage, string project = "demo") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Features:Storage"] = storage ? "true" : "false",
            ["Seed:Project"] = project,
        })
        .Build();

    private sealed class FakeReleases(SeedExtensionRelease? current) : ISeedExtensionReleases
    {
        public Task<SeedExtensionRelease?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(current);

        public Task<Stream?> OpenPackageAsync(string file, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(current is not null && file == current.File ? new MemoryStream(Encoding.ASCII.GetBytes("Cr24")) : null);
    }

    /// <summary>Jeder Abruf endet wie ein abgebrochener Blob-Download.</summary>
    private sealed class CancelledReleases : ISeedExtensionReleases
    {
        public Task<SeedExtensionRelease?> GetCurrentAsync(CancellationToken cancellationToken)
            => Task.FromException<SeedExtensionRelease?>(new TaskCanceledException());

        public Task<Stream?> OpenPackageAsync(string file, CancellationToken cancellationToken)
            => Task.FromException<Stream?>(new TaskCanceledException());
    }
}
