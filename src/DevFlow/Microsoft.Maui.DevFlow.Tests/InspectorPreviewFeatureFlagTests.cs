using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The Inspector page is one file for every configuration; what differs is what the broker tells it
/// about itself. These tests pin that contract from the outside: a default broker must serve only
/// the durable specialized surface, each preview flag must unlock exactly its own surface, and no
/// panel may be advertised whose route this layer does not actually serve.
/// </summary>
public sealed class InspectorPreviewFeatureFlagTests
{
    private const string WorkbenchMeta = "devflow-preview-workbench";
    private const string AgentAuthoringMeta = "devflow-preview-agent-authoring";
    private const string RepairMeta = "devflow-preview-repair";
    private const string SourceMeta = "devflow-preview-source";
    private const string TraceImportMeta = "devflow-preview-trace-import";

    [Fact]
    public async Task WithNoPreviewFlags_ThePageAdvertisesNoGenericTestManagementSurface()
    {
        var page = await RenderPageAsync(new MauiPreviewFeatureFlags());

        Assert.DoesNotContain(WorkbenchMeta, page, StringComparison.Ordinal);
        Assert.DoesNotContain(AgentAuthoringMeta, page, StringComparison.Ordinal);
        Assert.DoesNotContain(RepairMeta, page, StringComparison.Ordinal);
        Assert.DoesNotContain(SourceMeta, page, StringComparison.Ordinal);
        Assert.DoesNotContain(TraceImportMeta, page, StringComparison.Ordinal);
        // The durable Inspector surface is unconditional: it is what the product is without any
        // preview opt-in at all.
        Assert.Contains("devflow.js", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAgentAuthoringOnly_ThePageOffersApprovalsWithoutTheWorkbenchShell()
    {
        var page = await RenderPageAsync(new MauiPreviewFeatureFlags { AgentAuthoringEnabled = true });

        Assert.Contains(AgentAuthoringMeta, page, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkbenchMeta, page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithEveryPreviewFlag_ThePageAdvertisesEachSurfaceExactlyOnce()
    {
        var page = await RenderPageAsync(PreviewTestFeatures.AllEnabled());

        foreach (var meta in new[] { WorkbenchMeta, AgentAuthoringMeta, RepairMeta, SourceMeta, TraceImportMeta })
        {
            Assert.Equal(1, CountOccurrences(page, $"<meta name=\"{meta}\" content=\"true\">"));
        }
    }

    /// <summary>
    /// A kill switch has to reach the page, not just the routes. Otherwise the browser keeps
    /// offering a surface the server has already been told to refuse.
    /// </summary>
    [Fact]
    public async Task AKillSwitchedFlagIsNotAdvertisedToThePage()
    {
        var flags = PreviewTestFeatures.AllEnabled();
        flags.KillSwitches.Add("workbench");
        var page = await RenderPageAsync(flags);

        Assert.DoesNotContain(WorkbenchMeta, page, StringComparison.Ordinal);
        Assert.Contains(AgentAuthoringMeta, page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The browser hides an optional panel whose route is absent. This asserts the advertisement and
    /// the route agree, so the table cannot drift into promising a panel that answers 404.
    /// </summary>
    [Fact]
    public async Task EveryOptionalSurfaceIsAdvertisedExactlyWhenItsRouteIsServed()
    {
        Assert.NotEmpty(InspectorServer.OptionalSurfaces);
        await using var agent = new StubAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = CreateTokenClient(inspector);
            var page = await http.GetStringAsync($"http://127.0.0.1:{port}/");
            foreach (var surface in InspectorServer.OptionalSurfaces)
            {
                var advertised = page.Contains($"name=\"{surface.MetaName}\"", StringComparison.Ordinal);
                Assert.Equal(surface.Served, advertised);

                using var response = await http.PostAsync(
                    $"http://127.0.0.1:{port}{surface.Route}",
                    new StringContent("{}", Encoding.UTF8, "application/json"));
                var routed = response.StatusCode != HttpStatusCode.NotFound;
                Assert.Equal(surface.Served, routed);
            }
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    /// <summary>
    /// Every routed asset must exist as an embedded resource, and every embedded browser asset must
    /// be routed. A one-sided table either advertises a page the server cannot serve, or ships a
    /// module the page can never load.
    /// </summary>
    [Fact]
    public async Task AssetRoutesAndEmbeddedBrowserResourcesMatchExactly()
    {
        var assembly = typeof(InspectorServer).Assembly;
        const string prefix = "Microsoft.Maui.Cli.DevFlow.Inspector.Web.";
        var embedded = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            // inspector.html is rendered by HtmlRenderer, not served as a static asset.
            .Where(name => !string.Equals(name, "inspector.html", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(embedded);

        var routed = InspectorAssetResourceNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(embedded, routed);

        await using var agent = new StubAgent();
        var port = FreePort();
        var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            foreach (var path in InspectorAssetPaths())
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}{path}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
                var contentType = response.Content.Headers.ContentType?.MediaType;
                Assert.Equal(
                    path.EndsWith(".css", StringComparison.Ordinal) ? "text/css" : "application/javascript",
                    contentType);
            }
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    [Fact]
    public void EveryInspectorModuleReferencedByThePageIsRouted()
    {
        var assembly = typeof(InspectorServer).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Microsoft.Maui.Cli.DevFlow.Inspector.Web.inspector.html")!;
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();
        var routed = InspectorAssetPaths().ToHashSet(StringComparer.Ordinal);

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(html, "(?:src|href)=\"(?<file>[^\"]+\\.(?:js|css))\""))
        {
            var file = match.Groups["file"].Value;
            if (file.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            Assert.Contains("/" + file.TrimStart('.', '/'), routed);
        }
    }

    /// <summary>
    /// Repair and source review are separate advanced opt-ins. Enabling the guided workbench alone
    /// must not open a reviewed-patch surface, so each route is asserted against its own flag.
    /// </summary>
    [Theory]
    [InlineData("/api/workbench/repair/propose", "repair")]
    [InlineData("/api/workbench/repair/grant", "repair")]
    [InlineData("/api/workbench/repair/classify", "repair")]
    [InlineData("/api/workbench/source/propose", "source")]
    [InlineData("/api/workbench/source/analyze", "source")]
    [InlineData("/api/workbench/source/csharp/propose", "source")]
    public void RepairAndSourceRoutesFollowTheirOwnPreviewFlag(string route, string flag)
    {
        var workbenchOnly = new MauiPreviewFeatureFlags { WorkbenchEnabled = true, AgentAuthoringEnabled = true };
        Assert.False(IsPreviewRouteEnabled(workbenchOnly, route));

        var enabled = new MauiPreviewFeatureFlags
        {
            WorkbenchEnabled = true,
            RepairProposalsEnabled = flag == "repair",
            SourceProposalsEnabled = flag == "source",
        };
        Assert.True(IsPreviewRouteEnabled(enabled, route));
    }

    [Fact]
    public void TheGuidedRunSurfaceStillFollowsTheWorkbenchFlag()
    {
        Assert.False(IsPreviewRouteEnabled(new MauiPreviewFeatureFlags(), "/api/workbench/run/start"));
        Assert.True(IsPreviewRouteEnabled(
            new MauiPreviewFeatureFlags { WorkbenchEnabled = true },
            "/api/workbench/run/start"));
    }

    private static bool IsPreviewRouteEnabled(MauiPreviewFeatureFlags flags, string path)
        => (bool)typeof(InspectorServer)
            .GetMethod("IsPreviewRouteEnabled", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [flags, path])!;

    private static IEnumerable<string> InspectorAssetPaths() => AssetTable().Keys;

    private static IEnumerable<string> InspectorAssetResourceNames() =>
        AssetTable().Values.Select(asset => (string)asset
            .GetType()
            .GetProperty("ResourceName")!
            .GetValue(asset)!);

    private static IReadOnlyDictionary<string, object> AssetTable()
    {
        var field = typeof(InspectorServer)
            .GetField("Assets", BindingFlags.Static | BindingFlags.NonPublic)!;
        var table = (System.Collections.IDictionary)field.GetValue(null)!;
        var copy = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in table)
            copy[(string)entry.Key] = entry.Value!;
        return copy;
    }

    private static async Task<string> RenderPageAsync(MauiPreviewFeatureFlags flags)
    {
        await using var agent = new StubAgent();
        var port = FreePort();
        var inspector = new InspectorServer(
            port,
            "127.0.0.1",
            agent.Port,
            embedToken: null,
            agentId: "agent-1",
            appName: "Demo",
            platform: "android",
            project: "Demo.csproj",
            sessionId: "session-1",
            agentInstanceId: "instance-1",
            previewFlags: flags);
        inspector.Start();
        try
        {
            using var http = new HttpClient();
            return await http.GetStringAsync($"http://127.0.0.1:{port}/");
        }
        finally
        {
            await inspector.StopAsync();
        }
    }

    private static HttpClient CreateTokenClient(InspectorServer inspector)
    {
        var token = (string)typeof(InspectorServer)
            .GetField("_readToken", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inspector)!;
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-DevFlow-Inspector-Token", token);
        return http;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Minimal agent that answers just enough for the Inspector to render its page.</summary>
    private sealed class StubAgent : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public StubAgent()
        {
            Port = FreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        public int Port { get; }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }

                var body = Encoding.UTF8.GetBytes(
                    context.Request.Url?.AbsolutePath.Contains("tree", StringComparison.OrdinalIgnoreCase) == true
                        ? "{\"elements\":[]}"
                        : "{\"ok\":true}");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }
}
