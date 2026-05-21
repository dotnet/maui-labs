using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Exercises the DevFlow CLI verbs against a mock agent HTTP server to validate
/// correct route selection, methods, payloads, and query-parameter wiring.
/// </summary>
[Collection("CLI")]
public class DevFlowCliCommandTests
{
    private static async Task<(MockAgentServer server, CliTestHarness cli)> CreateFixturesAsync()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        return (server, cli);
    }

    // ========== extensions ==========

    [Fact]
    public async Task ExtensionsList_ReturnsRegisteredExtensions()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "extensions", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.True(json.GetProperty("extensions").TryGetProperty("com.example.diagnostics", out var extension));
        Assert.Equal("1.0.0", extension.GetProperty("version").GetString());
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/agent/capabilities");
    }

    [Fact]
    public async Task ExtensionsCall_PostsToolParameters()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync(
            "devflow", "extensions", "call",
            "com.example.diagnostics", "echo", """{"message":"hello"}""",
            "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("hello", json.GetProperty("body").GetProperty("message").GetString());
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ext/com.example.diagnostics/echo");
        Assert.Equal("POST", req.Method);
        Assert.Contains("hello", req.Body);
    }

    [Fact]
    public async Task ExtensionsDescribe_MissingExtension_WritesStructuredError()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "extensions", "describe", "com.example.missing", "--json");

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.StdErr);
        Assert.Equal("InvocationError", document.RootElement.GetProperty("type").GetString());
        Assert.Contains("was not found", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExtensionsCall_MissingTool_WritesStructuredError()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "extensions", "call", "com.example.diagnostics", "missing", "--json");

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.StdErr);
        Assert.Equal("InvocationError", document.RootElement.GetProperty("type").GetString());
        Assert.Contains("was not found", document.RootElement.GetProperty("error").GetString());
    }

    // ========== ui status / tree / query / element / hit-test ==========

    [Fact]
    public async Task UiTree_Default_HitsTreeRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "tree", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/tree");
    }

    [Fact]
    public async Task UiQuery_ByType_SendsTypeParam()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "query", "--type", "Button", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/elements");
        Assert.Contains("type=Button", req.QueryString);
    }

    [Fact]
    public async Task UiQuery_BySelector_SendsSelectorParam()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "query", "--selector", ".myClass", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/elements");
        Assert.Contains("selector=", req.QueryString);
    }

    [Fact]
    public async Task UiElement_ById_HitsElementRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "element", "el-123", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("el-123", json.GetProperty("id").GetString());
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/elements/el-123");
    }

    [Fact]
    public async Task UiHitTest_SendsXY()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "hit-test", "100", "200", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/hit-test");
        Assert.Contains("x=100", req.QueryString);
        Assert.Contains("y=200", req.QueryString);
    }

    [Fact]
    public async Task UiHitTest_AliasHittest_Works()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "hittest", "50", "60", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/hit-test");
    }

    // ========== ui action verbs ==========

    [Fact]
    public async Task UiFill_SendsFillAction()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "fill", "el-1", "hello", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/fill");
        Assert.Equal("POST", req.Method);
        Assert.Contains("hello", req.Body);
    }

    [Fact]
    public async Task UiClear_SendsClearAction()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "clear", "el-1", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/clear");
        Assert.Equal("POST", req.Method);
    }

    [Fact]
    public async Task UiFocus_SendsFocusAction()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "focus", "el-1", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/focus");
        Assert.Equal("POST", req.Method);
    }

    [Fact]
    public async Task UiNavigate_SendsNavigateAction()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "navigate", "//MainPage", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/navigate");
        Assert.Equal("POST", req.Method);
    }

    [Fact]
    public async Task UiScroll_SendsScrollAction()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "scroll", "--dy", "-100", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/scroll");
        Assert.Equal("POST", req.Method);
        Assert.Contains("-100", req.Body);
    }

    [Fact]
    public async Task UiResize_SendsResizeAction()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "resize", "800", "600", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/resize");
        Assert.Equal("POST", req.Method);
    }

    [Fact]
    public async Task UiProperty_Get_HitsPropertyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "property", "el-1", "Text", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path.Contains("/properties/"));
        Assert.Equal("GET", req.Method);
        Assert.Equal("/api/v1/ui/elements/el-1/properties/Text", req.Path);
    }

    [Fact]
    public async Task UiSetProperty_Put_HitsPropertyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "ui", "set-property", "el-1", "Text", "newvalue", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path.Contains("/properties/"));
        Assert.Equal("PUT", req.Method);
        Assert.Equal("/api/v1/ui/elements/el-1/properties/Text", req.Path);
        Assert.Contains("newvalue", req.Body);
    }

    // ========== logs ==========

    [Fact]
    public async Task Logs_HitsLogsRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "logs", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/logs");
        Assert.Equal("GET", req.Method);
    }

    // ========== network ==========

    [Fact]
    public async Task NetworkList_HitsRequestsRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "network", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/network/requests");
        Assert.Equal("GET", req.Method);
    }

    [Fact]
    public async Task NetworkDetail_HitsRequestByIdRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "network", "detail", "req-1", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path.StartsWith("/api/v1/network/requests/"));
        Assert.Equal("GET", req.Method);
        Assert.Equal("/api/v1/network/requests/req-1", req.Path);
    }

    [Fact]
    public async Task NetworkClear_DeletesRequests()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "network", "clear", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/network/requests");
        Assert.Equal("DELETE", req.Method);
    }

    // ========== preferences ==========

    [Fact]
    public async Task PreferencesList_HitsListRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "preferences", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/preferences");
        Assert.Equal("GET", req.Method);
    }

    [Fact]
    public async Task PreferencesGet_HitsKeyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "preferences", "get", "theme", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/preferences/theme");
        Assert.Equal("GET", req.Method);
    }

    [Fact]
    public async Task PreferencesDelete_HitsKeyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "preferences", "delete", "theme", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/preferences/theme");
        Assert.Equal("DELETE", req.Method);
    }

    [Fact]
    public async Task PreferencesClear_DeletesCollection()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "preferences", "clear", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/preferences");
        Assert.Equal("DELETE", req.Method);
    }

    // ========== secure storage ==========

    [Fact]
    public async Task SecureGet_HitsKeyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "secure-storage", "get", "authToken", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/secure/authToken");
        Assert.Equal("GET", req.Method);
    }

    [Fact]
    public async Task SecureSet_PutsKeyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "secure-storage", "set", "authToken", "my-token", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/secure/authToken");
        Assert.Equal("PUT", req.Method);
        Assert.Contains("my-token", req.Body);
    }

    [Fact]
    public async Task SecureDelete_DeletesKeyRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "secure-storage", "delete", "authToken", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/secure/authToken");
        Assert.Equal("DELETE", req.Method);
    }

    [Fact]
    public async Task SecureClear_DeletesCollection()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "storage", "secure-storage", "clear", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/secure");
        Assert.Equal("DELETE", req.Method);
    }

    // ========== theme ==========

    [Fact]
    public async Task ThemeGet_HitsThemeRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "theme", "get", "--json");

        Assert.True(result.ExitCode == 0, $"stdout: {result.StdOut}\nstderr: {result.StdErr}");
        var json = result.ParseJsonOutput();
        Assert.Equal("dark", json.GetProperty("theme").GetString());
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/app/theme");
        Assert.Equal("GET", req.Method);
    }

    [Fact]
    public async Task ThemeSet_DefaultAutoOnDesktop_UsesAppThemeRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "theme", "set", "dark", "--json");

        Assert.True(result.ExitCode == 0, $"stdout: {result.StdOut}\nstderr: {result.StdErr}");
        var json = result.ParseJsonOutput();
        Assert.Equal("dark", json.GetProperty("theme").GetString());
        Assert.Equal("app", json.GetProperty("source").GetString());
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/app/theme" && r.Method == "PUT");
        Assert.Contains("\"theme\":\"dark\"", req.Body);
    }

    [Theory]
    [InlineData("ios", "Virtual", null, null, true)]
    [InlineData("ios", "Physical", null, null, false)]
    [InlineData("ios", null, null, "A-SIMULATOR-UDID", true)]
    [InlineData("android", "Virtual", null, null, true)]
    [InlineData("android", "Physical", null, null, false)]
    [InlineData("android", null, "emulator-5554", null, true)]
    public void ThemeHostSelector_Auto_UsesHostOnlyForDisposableVirtualTargets(
        string platform,
        string? deviceType,
        string? androidDevice,
        string? simulatorUdid,
        bool expected)
    {
        var actual = ThemeHostSelector.ShouldUseHostThemeScopeAutomatically(
            platform,
            deviceType,
            DevFlowTheme.Dark,
            androidDevice,
            simulatorUdid);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ThemeHostSelector_Auto_DoesNotUseHostForSystemTheme()
    {
        var actual = ThemeHostSelector.ShouldUseHostThemeScopeAutomatically(
            "ios",
            "Virtual",
            DevFlowTheme.System,
            androidDevice: null,
            simulatorUdid: null);

        Assert.False(actual);
    }

    // ========== device/platform info ==========

    [Fact]
    public async Task DeviceApp_HitsAppRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "app-info", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/app");
    }

    [Fact]
    public async Task DeviceDisplay_HitsDisplayRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "display", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/display");
    }

    [Fact]
    public async Task DeviceBattery_HitsBatteryRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "battery", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/battery");
    }

    [Fact]
    public async Task DeviceConnectivity_HitsConnectivityRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "connectivity", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/connectivity");
    }

    [Fact]
    public async Task DeviceGeolocation_HitsGeolocationRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "geolocation", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/geolocation");
    }

    // ========== sensors ==========

    [Fact]
    public async Task SensorsList_HitsSensorsRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "sensors", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/sensors");
        Assert.Equal("GET", req.Method);
    }

    [Fact]
    public async Task SensorsStart_HitsStartRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "sensors", "start", "Accelerometer", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/sensors/Accelerometer/start");
        Assert.Equal("POST", req.Method);
    }

    [Fact]
    public async Task SensorsStop_HitsStopRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "device", "sensors", "stop", "Accelerometer", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/sensors/Accelerometer/stop");
        Assert.Equal("POST", req.Method);
    }

    // ========== webview / CDP ==========

    [Fact]
    public async Task WebViewRuntimeEvaluate_HitsEvaluateRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "webview", "Runtime", "evaluate", "1+1", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", req.Method);
        Assert.Contains("Runtime.evaluate", req.Body);
    }

    [Fact]
    public async Task WebViewDomGetDocument_HitsEvaluateRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "webview", "DOM", "getDocument", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", req.Method);
        Assert.Contains("DOM.getDocument", req.Body);
    }

    [Fact]
    public async Task WebViewPageReload_HitsEvaluateRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "webview", "Page", "reload", "--json");

        Assert.Equal(0, result.ExitCode);
        var req = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", req.Method);
        Assert.Contains("Page.reload", req.Body);
    }

    [Fact]
    public async Task WebViewWebViews_ListsContexts()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "webview", "webviews", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/contexts");
    }

    [Fact]
    public async Task WebViewSource_GetsSourceRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var _ = server;

        var result = await cli.InvokeAsync("devflow", "webview", "source", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/source");
    }
}
