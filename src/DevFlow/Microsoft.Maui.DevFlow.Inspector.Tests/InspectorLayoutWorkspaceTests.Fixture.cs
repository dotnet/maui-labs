using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>
/// Deterministic Chromium tests of the real InspectorServer shell and embedded JavaScript.
/// Only agent responses are fixtures; no broker, native app, or live-test opt-in is needed.
/// Build this project, install Chromium with its generated playwright.ps1, then run dotnet test.
/// </summary>
[Collection("Inspector")]
public partial class InspectorLayoutWorkspaceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;
    private InspectorServer _inspector = null!;
    private ShellAgent _agent = null!;
    private readonly List<string> _pageErrors = [];
    private readonly HashSet<string> _scripts = [];
    private string BaseUrl => $"http://127.0.0.1:{_inspector.Port}/";

    public async Task InitializeAsync()
    {
        _agent = new ShellAgent();
        _inspector = new InspectorServer(FreePort(), "127.0.0.1", _agent.Port);
        _inspector.Start();
        using var http = new HttpClient();
        var script = await http.GetStringAsync(BaseUrl + "inspector-layout.js");
        Assert.Contains("createLayoutPanel", script);

        _playwright = await Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        output.WriteLine($"Headless Chromium {_browser.Version}; real InspectorServer {BaseUrl}");
        _context = await _browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }
        });
        _page = await _context.NewPageAsync();
        _page.PageError += (_, error) => _pageErrors.Add(error);
        _page.Response += (_, response) =>
        {
            if (response.Ok && response.Url.EndsWith(".js", StringComparison.Ordinal))
                _scripts.Add(new Uri(response.Url).AbsolutePath);
        };
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_inspector is not null)
        {
            await _inspector.StopAsync();
            _inspector.Dispose();
        }
        if (_agent is not null) await _agent.DisposeAsync();
        output.WriteLine($"Loaded embedded scripts: {string.Join(", ", _scripts.Order())}");
        Assert.Contains("/devflow.js", _scripts);
        Assert.Contains("/inspector-layout.js", _scripts);
        Assert.Empty(_pageErrors);
    }

    private Task CaptureClipboardWritesAsync() =>
        _page.AddInitScriptAsync("""
            window.__copiedDevFlowData = "";
            Object.defineProperty(navigator, "clipboard", {
              configurable: true,
              value: { writeText: async text => { window.__copiedDevFlowData = text; } }
            });
            """);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Mirrors main's InspectorDiagnosticsOptInTests agent: enough real HTTP tree/screenshot
    // responses to render the shell. Each browser test intercepts its scenario-specific APIs.
    internal sealed class ShellAgent : IAsyncDisposable
    {
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        private readonly HttpListener _listener = new();
        private readonly Task _loop;
        private readonly string _appName;
        private readonly bool _failNavigation;
        private string? _heldLeaseId;
        public int Port { get; } = FreePort();
        public string? HeldLeaseId => Volatile.Read(ref _heldLeaseId);
        public ConcurrentQueue<(string Method, string Path, string Body)> Requests { get; } = new();

        public ShellAgent(string appName = "Layout fixture", bool failNavigation = false)
        {
            _appName = appName;
            _failNavigation = failNavigation;
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _loop = ServeAsync();
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
                using var response = context.Response;
                var path = context.Request.Url!.AbsolutePath;
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                Requests.Enqueue((context.Request.HttpMethod, path, body));
                var screenshot = path == "/api/v1/ui/screenshot";
                var payload = screenshot ? Png : Encoding.UTF8.GetBytes(path switch
                {
                    "/api/v1/ui/tree" => """
                        {"revision":"fixture-shell","elements":[
                          {"id":"layout-root","type":"ContentPage","isVisible":true,"isEnabled":true,
                           "bounds":{"x":0,"y":0,"width":320,"height":600},
                           "windowBounds":{"x":0,"y":0,"width":320,"height":600}}]}
                        """,
                    "/api/v1/agent/lease" => LeaseResponse(body),
                    "/api/v1/agent/status" => JsonSerializer.Serialize(new
                    {
                        running = true, app = new { name = _appName }, device = new { platform = "test" }
                    }),
                    "/api/v1/ui/actions/navigate" => JsonSerializer.Serialize(new { success = !_failNavigation }),
                    "/api/v1/agent/logs" => "[]",
                    _ => "{}"
                });
                response.ContentType = screenshot ? "image/png" : "application/json";
                response.ContentLength64 = payload.Length;
                await response.OutputStream.WriteAsync(payload);
            }
        }

        private string LeaseResponse(string body)
        {
            using var request = JsonDocument.Parse(body);
            var action = request.RootElement.GetProperty("action").GetString();
            var leaseId = request.RootElement.GetProperty("leaseId").GetString();
            if (action == "claim")
                Volatile.Write(ref _heldLeaseId, leaseId);
            else if (action == "release" && HeldLeaseId == leaseId)
                Volatile.Write(ref _heldLeaseId, null);
            return JsonSerializer.Serialize(new
            {
                ok = true, allowed = true, youHold = HeldLeaseId == leaseId, heldByOther = false
            });
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Close();
            await _loop;
        }
    }
}
