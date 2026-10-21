using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using SkiaSharp;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

public class WebViewHttpFixtureTests
{
    [Fact]
    public void AndroidHttpPolicy_AllowsOnlyTheOwnedFixtureLoopbackHosts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MauiLabs.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var android = Path.Combine(root.FullName, "samples", "DevFlow.Sample", "Platforms", "Android");
        var manifest = XDocument.Load(Path.Combine(android, "AndroidManifest.xml"));
        XNamespace ns = "http://schemas.android.com/apk/res/android";
        var application = Assert.Single(manifest.Root!.Elements("application"));
        Assert.Null(application.Attribute(ns + "usesCleartextTraffic"));
        Assert.Equal("@xml/network_security_config", application.Attribute(ns + "networkSecurityConfig")?.Value);
        var policy = XDocument.Load(Path.Combine(android, "Resources", "xml", "network_security_config.xml"));
        Assert.Empty(policy.Root!.Elements("base-config"));
        var domainConfig = Assert.Single(policy.Root.Elements("domain-config"));
        Assert.Equal("true", domainConfig.Attribute("cleartextTrafficPermitted")?.Value);
        Assert.Equal(new[] { "10.0.2.2", "127.0.0.1" },
            domainConfig.Elements("domain").Select(domain => domain.Value).Order());
        Assert.All(domainConfig.Elements("domain"),
            domain => Assert.Equal("false", domain.Attribute("includeSubdomains")?.Value));
    }

    [Fact]
    public void PngDecoder_ReadsEncodedPixelsInTheTestHost()
    {
        using var source = new SKBitmap(80, 80);
        source.Erase(new SKColor(226, 244, 231));
        source.SetPixel(20, 20, SKColors.Red);
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(data.ToArray());

        Assert.NotNull(decoded);
        Assert.Equal(80, decoded.Width);
        Assert.Equal(80, decoded.Height);
        Assert.Equal(SKColors.Red, decoded.GetPixel(20, 20));
        Assert.Equal(new SKColor(226, 244, 231), decoded.GetPixel(60, 60));
    }

    [Fact]
    public async Task ClientReset_IncompleteRequest_DoesNotStopOtherConnectionsOrDisposal()
    {
        var fixture = new WebViewHttpFixture();
        var port = fixture.Port;
        try
        {
            using var idle = new TcpClient();
            await idle.ConnectAsync("127.0.0.1", port);
            for (var attempt = 1; attempt <= 20; attempt++)
            {
                using var abandoned = new TcpClient();
                await abandoned.ConnectAsync("127.0.0.1", port);
                await WaitForAsync(() => fixture.AcceptedConnectionCount == attempt + 1);
                await abandoned.GetStream().WriteAsync(
                    System.Text.Encoding.ASCII.GetBytes("GET /index.html HTTP/1.1\r\nX-Abandoned: "));
                await WaitForAsync(() => fixture.StartedRequestCount == attempt);
                abandoned.Client.LingerState = new LingerOption(true, 0);
                abandoned.Client.Close();
                await WaitForAsync(() => fixture.ClientDisconnects.Count == attempt);
            }
            Assert.All(fixture.ClientDisconnects, error =>
                Assert.True(error is SocketError.ConnectionReset or SocketError.ConnectionAborted, error.ToString()));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = await http.GetStringAsync(fixture.GetUrl("maccatalyst"));
            Assert.Contains("HTTP WebView fixture", body);
            Assert.Contains("fixture-button", body);
            await WaitForAsync(() => !fixture.ServedResponses.IsEmpty);
            var served = Assert.Single(fixture.ServedResponses);
            Assert.Equal("/index.html", served.Path);
            Assert.Equal(200, served.StatusCode);
            Assert.Equal(body, served.Body);
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(body), served.BodyLength);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(body))), served.BodySha256);
            using var missing = await http.GetAsync($"http://127.0.0.1:{port}/missing.html");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
        using var socket = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => socket.ConnectAsync("127.0.0.1", port));
    }

    static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(25, timeout.Token);
    }
}
