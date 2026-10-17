using System.Xml.Linq;
using Microsoft.AspNetCore.Http;

namespace Bfs.Seed.Extension.Tests;

public class SeedExtensionTests
{
    [Fact]
    public void UpdateManifest_IsGupdateWithOneApp()
    {
        var release = new SeedExtensionRelease("flpkgokniclbiocmpjpmficmfcdghfgd", "1.1.0.2190", "sentinel-link-1.1.0.2190.crx", "ABCDEF0123");

        var xml = XDocument.Parse(SeedExtension.UpdateManifest(release, "https://api.example/api/extension/packages/sentinel-link-1.1.0.2190.crx"));

        XNamespace ns = "http://www.google.com/update2/response";
        Assert.Equal(ns + "gupdate", xml.Root!.Name);
        Assert.Equal("2.0", xml.Root.Attribute("protocol")!.Value);
        var app = Assert.Single(xml.Root.Elements(ns + "app"));
        Assert.Equal("flpkgokniclbiocmpjpmficmfcdghfgd", app.Attribute("appid")!.Value);
        var check = app.Element(ns + "updatecheck")!;
        Assert.Equal("1.1.0.2190", check.Attribute("version")!.Value);
        Assert.Equal("abcdef0123", check.Attribute("hash_sha256")!.Value);
        Assert.Equal("https://api.example/api/extension/packages/sentinel-link-1.1.0.2190.crx", check.Attribute("codebase")!.Value);
    }

    [Theory]
    [InlineData("func-x.azurewebsites.net", "http", "https://func-x.azurewebsites.net/api/extension/packages/a-1.0.crx")]
    [InlineData("localhost:7071", "http", "http://localhost:7071/api/extension/packages/a-1.0.crx")]
    [InlineData("127.0.0.1:7071", "http", "http://127.0.0.1:7071/api/extension/packages/a-1.0.crx")]
    public void PackageUrl_IsHttpsExceptLocally(string host, string scheme, string expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString(host);
        request.Scheme = scheme;

        Assert.Equal(expected, SeedExtension.PackageUrl(request, "a-1.0.crx"));
    }

    [Theory]
    [InlineData("sentinel-auth-1.0.0.2210.crx", true)]
    [InlineData("sentinel-auth-1.crx", true)]
    [InlineData("sentinel-auth-65535.65535.65535.65535.crx", true)]
    [InlineData("sentinel-auth-1.0.0.0.0.crx", false)]
    [InlineData("sentinel-auth-123456.crx", false)]
    [InlineData("sentinel-auth-1.0.crx.json", false)]
    [InlineData("sentinel-link-1.0.crx", false)]
    [InlineData("SENTINEL-AUTH-1.0.crx", false)]
    [InlineData("current.json", false)]
    [InlineData("../sentinel-auth-1.0.crx", false)]
    [InlineData("sentinel-auth-1.0.crx/../current.json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPackageFile_AcceptsOnlyNamesThePipelineGives(string? file, bool expected)
    {
        Assert.Equal(expected, SeedExtension.IsPackageFile("sentinel-auth", file));
    }

    [Theory]
    [InlineData("sentinel-auth", true)]
    [InlineData("a", true)]
    [InlineData("Sentinel-Auth", false)]
    [InlineData("-x", false)]
    [InlineData("a.b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidPackageName(string? name, bool expected)
    {
        Assert.Equal(expected, SeedExtension.IsValidPackageName(name));
    }
}
