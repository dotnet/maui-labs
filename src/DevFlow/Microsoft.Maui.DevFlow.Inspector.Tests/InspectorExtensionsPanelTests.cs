using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Extensibility;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>
/// Headless Chromium against the real InspectorServer and shipped JavaScript, backed by a real
/// UI-less DevFlow agent that hosts a plugin. Covers the generic Extensions tab: descriptor listing,
/// load errors, schema-generated tool forms, and live stream tables over the /ws/extensions proxy.
/// </summary>
[Collection("Inspector")]
public sealed class InspectorExtensionsPanelTests(ITestOutputHelper output) : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;
    private DevFlowAgentService _agent = null!;
    private InspectorServer _inspector = null!;
    private readonly BlePlugin _plugin = new();
    private readonly List<string> _pageErrors = [];
    private string BaseUrl => $"http://127.0.0.1:{_inspector.Port}/";

    public async Task InitializeAsync()
    {
        var agentPort = FreePort();
        var options = new AgentOptions { Port = agentPort };
        options.AddExtension(_plugin);
        options.AddExtension(_ => throw new InvalidOperationException("no adapter"));
        _agent = new DevFlowAgentService(options);
        _agent.StartServerOnly(null);

        _inspector = new InspectorServer(FreePort(), "localhost", agentPort);
        _inspector.Start();

        _playwright = await Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        output.WriteLine($"Headless Chromium {_browser.Version}; real InspectorServer {BaseUrl}");
        var context = await _browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        _page = await context.NewPageAsync();
        _page.PageError += (_, error) => _pageErrors.Add(error);
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_inspector is not null)
        {
            await _inspector.StopAsync();
            _inspector.Dispose();
        }
        _agent?.Dispose();
        Assert.Empty(_pageErrors);
    }

    [InspectorBrowserFact]
    public async Task ExtensionsTab_ListsPlugins_RunsTools_AndStreamsEvents()
    {
        // Overflow the 3-event buffer so the first (replaying) connection reports dropped events.
        for (var i = 1; i <= 5; i++)
            _plugin.Events.Publish(new BleEvent("scan", $"Sensor {i}", $"-{60 + i} dBm"));

        await _page.GotoAsync(BaseUrl);
        var tab = _page.Locator("#df-tab-extensions");
        await _page.Locator("#df-toggle-dock").ClickAsync();
        await Expect(tab).ToBeVisibleAsync();
        await tab.ClickAsync();
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true");

        var body = _page.Locator("#df-dock-body");
        await Expect(body.Locator(".df-ext-rail-item")).ToHaveCountAsync(2);
        await Expect(body.Locator(".df-ext-title")).ToHaveTextAsync("org.example.ble");
        await Expect(body.Locator(".df-ext-header")).ToContainTextAsync("v1.2.0");
        await Expect(body.Locator(".df-ext-header")).ToContainTextAsync("ready");

        // Schema → form: run a read-only tool with a string and an integer parameter.
        var tool = body.Locator("details.df-ext-tool", new() { HasText = "peripherals" });
        await tool.Locator("summary").ClickAsync();
        await tool.GetByLabel("filter").FillAsync("Polar");
        await tool.GetByLabel("limit").FillAsync("2.5");
        await tool.GetByRole(AriaRole.Button, new() { Name = "Run" }).ClickAsync();
        await Expect(tool.Locator(".df-ext-error")).ToContainTextAsync("whole number");
        await tool.GetByLabel("limit").FillAsync("2");
        await tool.GetByRole(AriaRole.Button, new() { Name = "Run" }).ClickAsync();
        await Expect(tool.Locator(".df-ext-meta")).ToContainTextAsync("200 OK");
        await Expect(tool.Locator(".df-ext-output")).ToContainTextAsync("\"filter\": \"Polar\"");
        await Expect(tool.Locator(".df-ext-output")).ToContainTextAsync("\"limit\": \"2\"");

        // Stream: buffered replay (with the dropped note), then a live event; filter narrows rows.
        var stream = body.Locator(".df-ext-stream");
        await Expect(stream.Locator(".df-ext-live")).ToHaveTextAsync("live");
        await Expect(stream.Locator(".df-ext-dropped")).ToHaveTextAsync("2 events dropped");
        await Expect(stream.Locator("tbody tr:not(:has(.df-ext-marker))")).ToHaveCountAsync(3);
        await Expect(stream.Locator("thead th")).ToHaveTextAsync(["#", "Time", "kind", "peripheral", "detail"]);

        _plugin.Events.Publish(new BleEvent("connect", "Polar H10", "connected"));
        await Expect(stream.Locator("tbody")).ToContainTextAsync("Polar H10");
        await stream.GetByPlaceholder("Filter events").FillAsync("polar");
        await Expect(stream.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(stream.Locator(".df-ext-meta")).ToHaveTextAsync("1 of 4 events");

        // Paused events are held back until resume.
        await stream.GetByRole(AriaRole.Button, new() { Name = "Pause" }).ClickAsync();
        _plugin.Events.Publish(new BleEvent("disconnect", "Polar H10", null));
        await Expect(stream.Locator(".df-ext-live")).ToHaveTextAsync("paused");
        await stream.GetByPlaceholder("Filter events").FillAsync("");
        await Expect(stream.Locator(".df-ext-meta")).ToContainTextAsync("1 pending");
        await stream.GetByRole(AriaRole.Button, new() { Name = "Resume" }).ClickAsync();
        await Expect(stream.Locator("tbody")).ToContainTextAsync("disconnect");

        // Load errors from plugins that could not be created.
        await body.Locator(".df-ext-rail-item", new() { HasText = "Load errors" }).ClickAsync();
        await Expect(body.Locator(".df-ext-detail")).ToContainTextAsync("no adapter");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public sealed record BleEvent(string Kind, string Peripheral, string? Detail);

    public sealed class BlePlugin() : DevFlowExtension("org.example.ble", "BLE monitor", "1.2.0")
    {
        public DevFlowEventStream<BleEvent> Events { get; } = new(3);

        protected override void Configure(IDevFlowExtensionBuilder builder)
        {
            using var schema = System.Text.Json.JsonDocument.Parse("""
                {"type":"object","properties":{
                  "filter":{"type":"string","description":"Name filter"},
                  "limit":{"type":"integer"}}}
                """);
            builder.MapTool("peripherals", "Known peripherals.", "GET", "/peripherals",
                request => Task.FromResult(DevFlowResponse.Json(new
                {
                    filter = request.GetParameter("filter"),
                    limit = request.GetParameter("limit")
                })),
                parameters: schema.RootElement.Clone(),
                annotations: new DevFlowToolAnnotations { ReadOnly = true });
            builder.MapStream("events", "Adapter and peripheral events.", Events, ["kind", "peripheral", "detail"]);
        }
    }
}
