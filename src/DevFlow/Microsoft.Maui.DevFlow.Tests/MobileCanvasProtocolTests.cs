using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Contract tests against a stub that speaks the external device host's real wire protocol.
/// <para>
/// These exist because the adapter's failure mode is silence: a wrong field name, a wrong route,
/// or a missing credential makes every request fail in a way that is indistinguishable from
/// "no device layer installed". Degradation tests alone cannot catch that, because a broken
/// adapter degrades beautifully. Only a successful authenticated round trip proves the binding.
/// </para>
/// </summary>
public class MobileCanvasProtocolTests : IAsyncLifetime
{
    private HttpListener _listener = null!;
    private string _controlToken = "test-control-token";
    private int _port;
    private readonly List<(string Method, string Path, string? Authorization)> _requests = [];
    private const string DeviceId = "ios:simulator:A1B2";

    /// <summary>
    /// A recording path the stub host is allowed to report. DevFlow names the file itself and
    /// refuses anything outside its own recordings root, so the stub has to answer with a contained
    /// path exactly as a real host would.
    /// </summary>
    private static readonly string ContainedRecordingPath =
        Path.Combine(MobileCanvasDeviceSurface.RecordingRoot, "protocol-contract.mp4");
    private string _uiTapResponse = """{"success":true,"total":1}""";

    public Task InitializeAsync()
    {
        _port = GetFreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { _listener.Stop(); } catch { }
        return Task.CompletedTask;
    }

    private static int GetFreePort() => TestPorts.Reserve();

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; }

            // ASP.NET Core decodes route parameters, so the real host sees the raw device id.
            var path = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/");
            var authorization = context.Request.Headers["Authorization"];
            lock (_requests) _requests.Add((context.Request.HttpMethod, path, authorization));

            // The real host authenticates every control request with a bearer token.
            if (authorization != $"Bearer {_controlToken}")
            {
                context.Response.StatusCode = 401;
                context.Response.Close();
                continue;
            }

            if (path == $"/api/v1/devices/{DeviceId}/screenshot")
            {
                var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 1, 2, 3, 4 };
                context.Response.StatusCode = 200;
                context.Response.ContentType = "image/png";
                context.Response.ContentLength64 = png.Length;
                await context.Response.OutputStream.WriteAsync(png);
                context.Response.Close();
                continue;
            }

            if (path == "/api/v1/devices" && context.Request.HttpMethod == "POST")
            {
                await WriteJsonAsync(context, 200, DeviceJson("created-device"));
                continue;
            }
            if (path == $"/api/v1/devices/{DeviceId}" && context.Request.HttpMethod == "DELETE")
            {
                await WriteJsonAsync(context, 200, """{"success":true,"operation":"delete"}""");
                continue;
            }

            var (status, body) = path switch
            {
                "/api/v1/status" => (200, """{"status":"ok","version":"0.1.16","processId":42}"""),
                "/api/v1/catalog" => (200, $$"""
                    {"schemaVersion":"1.0","devices":[{{DeviceJsonLiteral}}],
                     "runtimes":[{"id":"runtime-1","name":"iOS 18","version":"18.0","platform":"ios",
                                  "isAvailable":true,"supportedDeviceTypeIds":["type-1"]}],
                     "deviceTypes":[{"id":"type-1","name":"iPhone 16","platform":"ios"}],
                     "diagnostics":[{"platform":"ios","ready":true,"checks":[]}]}
                    """),
                "/api/v1/devices" => (200, """
                    [{"schemaVersion":"1.0","id":"ios:simulator:A1B2","platform":"ios","provider":"simulator",
                      "nativeId":"A1B2","udid":"A1B2","name":"iPhone 16",
                      "state":"booted","isAvailable":true,
                      "display":{"pixelWidth":1170,"pixelHeight":2532,"pointWidth":390,"pointHeight":844,"scale":3,"orientation":"portrait"},
                      "capabilities":{"tap":true,"screenshot":true,"liveStream":true,"boot":true,"shutdown":true}}]
                    """),
                $"/api/v1/devices/{DeviceId}" => (200, """
                    {"schemaVersion":"1.0","id":"ios:simulator:A1B2","platform":"ios","provider":"simulator",
                     "nativeId":"A1B2","udid":"A1B2","name":"iPhone 16","state":"booted","isAvailable":true,
                     "display":{"pixelWidth":2532,"pixelHeight":1170,"pointWidth":844,"pointHeight":390,
                                "scale":3,"orientation":"landscape-left"},
                     "capabilities":{"tap":true,"screenshot":true,"liveStream":true,"boot":true,"shutdown":true}}
                    """),
                $"/api/v1/devices/{DeviceId}/boot" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/shutdown" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/restart" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/reveal" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/erase" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/input/tap" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/input/swipe" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/input/text" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/input/key" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/input/button" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/permissions" => (200, """{"success":true}"""),
                $"/api/v1/devices/{DeviceId}/hardware/location" => (200, """{"operation":"location-set"}"""),
                $"/api/v1/devices/{DeviceId}/hardware/battery" => (200, """{"batteryLevel":5,"batteryState":"discharging"}"""),
                $"/api/v1/devices/{DeviceId}/hardware/network" => (200, """{"networkIsIndicatorOnly":false}"""),
                $"/api/v1/devices/{DeviceId}/input/rotate" => (200, "{}"),
                $"/api/v1/devices/{DeviceId}/recording/start" => (200, $$"""{"isRecording":true,"outputPath":{{JsonSerializer.Serialize(ContainedRecordingPath)}}}"""),
                $"/api/v1/devices/{DeviceId}/recording/stop" => (200, $$"""{"isRecording":false,"outputPath":{{JsonSerializer.Serialize(ContainedRecordingPath)}}}"""),
                $"/api/v1/devices/{DeviceId}/recording" => (200, """{"deviceId":"ios:simulator:A1B2","isRecording":true,"startedAt":"2026-08-22T10:00:00Z","timeoutSeconds":180}"""),
                $"/api/v1/devices/{DeviceId}/ui/tap" => (200, _uiTapResponse),
                $"/api/v1/devices/{DeviceId}/ui/snapshot" => (200,
                    """
                    {"deviceId":"ios:simulator:A1B2","capturedAt":"2026-08-25T10:00:00Z",
                     "orientation":"portrait","scale":3,"foregroundOwner":"com.apple.springboard",
                     "keyboardVisible":true,
                     "elements":[{"id":"keyboard","role":"keyboard","type":"XCUIElementTypeKeyboard",
                                  "packageId":"com.apple.springboard","isSystem":true,"interactive":true,
                                  "bounds":{"x":0,"y":500,"width":390,"height":344}}],
                     "limitations":[]}
                    """),
                _ => (404, """{"error":"not found"}"""),
            };

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private const string DeviceJsonLiteral = """
        {"schemaVersion":"1.0","id":"ios:simulator:A1B2","platform":"ios","provider":"simulator",
         "nativeId":"A1B2","udid":"A1B2","name":"iPhone 16","state":"booted","isAvailable":true,
         "runtimeId":"runtime-1","runtimeName":"iOS 18","deviceTypeId":"type-1","deviceTypeName":"iPhone 16",
         "display":{"pixelWidth":1170,"pixelHeight":2532,"pointWidth":390,"pointHeight":844,"scale":3,"orientation":"portrait"},
         "capabilities":{"boot":true,"shutdown":true,"restart":true,"erase":true,"delete":true,"reveal":true,
                         "tap":true,"longPress":true,"swipe":true,"text":true,"key":true,"button":true,
                         "rotate":true,"screenshot":true,"liveStream":true,"recording":true}}
        """;

    private static string DeviceJson(string id) =>
        DeviceJsonLiteral.Replace("\"ios:simulator:A1B2\"", $"\"{id}\"", StringComparison.Ordinal);

    private static async Task WriteJsonAsync(HttpListenerContext context, int status, string body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private MobileCanvasDeviceSurface CreateSurface(string? token = null, string? schemaVersion = "1.0") =>
        new(stateProvider: () => new MobileCanvasHostState
        {
            Port = _port,
            ProcessId = 42,
            Version = MobileCanvasProtocol.ValidatedHostVersion,
            ControlToken = token ?? _controlToken,
            SchemaVersion = schemaVersion,
        });

    [Fact]
    public void CompatibilityManifest_PinsTheValidatedUpstreamRevision()
    {
        Assert.Equal("0.1.16", MobileCanvasProtocol.ValidatedHostVersion);
        Assert.Equal("1.0", MobileCanvasProtocol.ValidatedProtocolVersion);
        Assert.Equal(40, MobileCanvasProtocol.ValidatedHostRevision.Length);
    }

    [Fact]
    public async Task Health_SucceedsAgainstTheRealStatusRoute()
    {
        using var surface = CreateSurface();

        var health = await surface.GetHealthAsync();

        Assert.True(health.Available);
        Assert.Equal(DeviceHostAvailability.Available, health.Availability);

        // Pin the route: the host serves /status, not /health.
        lock (_requests)
            Assert.Contains(_requests, r => r.Path == "/api/v1/status");
    }

    [Fact]
    public async Task EveryRequest_CarriesTheControlToken()
    {
        using var surface = CreateSurface();

        await surface.GetHealthAsync();
        await surface.ListAsync();

        lock (_requests)
            Assert.All(_requests, r => Assert.Equal($"Bearer {_controlToken}", r.Authorization));
    }

    [Fact]
    public async Task ListDevices_DeserialisesTheRealPayload()
    {
        using var surface = CreateSurface();

        var devices = await surface.ListAsync();

        Assert.NotNull(devices);
        var device = Assert.Single(devices);
        Assert.Equal(DeviceId, device.Id);
        Assert.Equal("simulator", device.Provider);
        Assert.Equal("A1B2", device.NativeId);
        Assert.Equal("iPhone 16", device.Name);
        Assert.True(device.IsBooted);
        Assert.True(device.Capabilities.Tap);
        Assert.True(device.Capabilities.LiveStream);
        Assert.Equal(390, device.Display!.PointWidth);
        Assert.Equal(3, device.Display.Scale);
    }

    [Fact]
    public async Task PairedDevice_BuildsAUsableCoordinateSpace()
    {
        // The end-to-end point: a device discovered over the wire must yield a coordinate space
        // that can actually place a tap.
        using var surface = CreateSurface();
        var devices = await surface.ListAsync();
        Assert.NotNull(devices);
        var device = Assert.Single(devices);

        var space = DeviceCoordinateSpace.FromDisplay(device.Display!);
        var app = space.FrameToApp(space.AppToFrame(new AppPoint(100, 200)));

        Assert.NotNull(app);
        Assert.Equal(100, app!.Value.X, 1e-9);
        Assert.Equal(200, app.Value.Y, 1e-9);
    }

    [Fact]
    public async Task Boot_SucceedsAgainstTheRealRoute()
    {
        using var surface = CreateSurface();

        var result = await surface.BootAsync(DeviceId);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CoreControlAndMediaRoutes_MatchTheValidatedHost()
    {
        using var surface = CreateSurface();

        Assert.NotNull(await surface.GetAsync(DeviceId));
        Assert.True((await surface.BootAsync(DeviceId)).Success);
        Assert.True((await surface.ShutdownAsync(DeviceId)).Success);
        Assert.True((await surface.TapAsync(DeviceId, new DevicePoint(10, 20))).Success);
        Assert.NotNull(await surface.ScreenshotAsync(DeviceId));
        var ui = await surface.CaptureUiAsync(DeviceId, "com.example.app");
        Assert.NotNull(ui);
        Assert.True(ui!.KeyboardVisible);
        Assert.Equal("com.apple.springboard", ui.ForegroundOwner);
        Assert.Equal("keyboard", Assert.Single(ui.Elements).Id);
    }

    [Fact]
    public async Task EnvironmentRecordingAndNativeUiRoutes_MatchTheValidatedHost()
    {
        using var surface = CreateSurface();

        Assert.True((await surface.SetPermissionAsync(
            DeviceId,
            "com.example.app",
            "camera",
            "granted")).Success);
        Assert.True((await surface.SetLocationAsync(
            DeviceId,
            new DeviceLocation { Latitude = 51.5, Longitude = -0.12 })).Success);
        Assert.True((await surface.SetBatteryAsync(DeviceId, 5)).Success);
        Assert.True((await surface.SetNetworkAsync(DeviceId, "online")).Success);
        Assert.True((await surface.RotateAsync(DeviceId, DeviceOrientations.LandscapeLeft)).Success);
        Assert.True((await surface.StartRecordingAsync(DeviceId)).Success);
        Assert.True((await surface.StopRecordingAsync(DeviceId)).Success);
        Assert.True((await surface.TapUiAsync(DeviceId, "allow-button", null)).Success);
    }

    [Fact]
    public async Task NativeUiTap_ClassifiesAConfirmedZeroMatchAsNotFound()
    {
        _uiTapResponse = """{"success":false,"total":0}""";
        using var surface = CreateSurface();

        var result = await surface.TapUiAsync(DeviceId, "missing", null);

        Assert.False(result.Success);
        Assert.Equal(DeviceOperationFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task NativeUiTap_ClassifiesAnAcceptedEmptyResponseAsUnknownCompletion()
    {
        _uiTapResponse = "";
        using var surface = CreateSurface();

        var result = await surface.TapUiAsync(DeviceId, "allow-button", null);

        Assert.False(result.Success);
        Assert.Equal(DeviceOperationFailureKind.UnknownCompletion, result.FailureKind);
    }

    [Fact]
    public async Task CompleteCanvasActionSurface_MatchesTheValidatedHost()
    {
        using var surface = CreateSurface();

        var catalog = await surface.GetCatalogAsync();
        Assert.NotNull(catalog);
        Assert.Single(catalog.Runtimes);
        Assert.Single(catalog.DeviceTypes);
        Assert.True((await surface.CreateAsync(new DeviceCreateRequest
        {
            Platform = DevicePlatforms.Ios,
            Name = "Created",
            RuntimeId = "runtime-1",
            DeviceTypeId = "type-1",
        })).Success);
        Assert.True((await surface.RestartAsync(DeviceId)).Success);
        Assert.True((await surface.RevealAsync(DeviceId)).Success);
        Assert.True((await surface.EraseAsync(DeviceId, confirm: true)).Success);
        Assert.True((await surface.DeleteAsync(DeviceId, confirm: true)).Success);
        Assert.True((await surface.LongPressAsync(DeviceId, new DevicePoint(10, 20), 1)).Success);
        Assert.True((await surface.SwipeAsync(DeviceId, new DeviceSwipe
        {
            StartX = 1,
            StartY = 2,
            EndX = 3,
            EndY = 4,
            Duration = 0.35,
        })).Success);
        Assert.True((await surface.TypeTextAsync(DeviceId, "hello")).Success);
        Assert.True((await surface.PressKeyAsync(DeviceId, 40)).Success);
        Assert.True((await surface.PressButtonAsync(DeviceId, "home")).Success);
        Assert.True((await surface.GetRecordingStatusAsync(DeviceId))!.IsRecording);

        lock (_requests)
        {
            Assert.Contains(_requests, request => request.Method == "POST" && request.Path == "/api/v1/devices");
            Assert.Contains(_requests, request => request.Method == "DELETE" && request.Path == $"/api/v1/devices/{DeviceId}");
            Assert.Contains(_requests, request => request.Path.EndsWith("/input/swipe", StringComparison.Ordinal));
            Assert.Contains(_requests, request => request.Path.EndsWith("/input/text", StringComparison.Ordinal));
            Assert.Contains(_requests, request => request.Path.EndsWith("/input/key", StringComparison.Ordinal));
            Assert.Contains(_requests, request => request.Path.EndsWith("/input/button", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task DestructiveOperations_RequireExplicitConfirmationBeforeCallingHost()
    {
        using var surface = CreateSurface();
        int before;
        lock (_requests) before = _requests.Count;

        Assert.False((await surface.EraseAsync(DeviceId, confirm: false)).Success);
        Assert.False((await surface.DeleteAsync(DeviceId, confirm: false)).Success);

        lock (_requests)
            Assert.Equal(before, _requests.Count);
    }

    [Fact]
    public async Task WrongToken_ReportsUnauthorized_NotAbsent()
    {
        // The critical distinction: an authentication failure must not impersonate "no device
        // layer installed", or a fixable integration break silently presents as a missing feature.
        using var surface = CreateSurface(token: "wrong-token");

        var health = await surface.GetHealthAsync();

        Assert.False(health.Available);
        Assert.Equal(DeviceHostAvailability.Unauthorized, health.Availability);
        Assert.Contains("control token", health.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongToken_ReportsRejection_OnControlOperations()
    {
        using var surface = CreateSurface(token: "wrong-token");

        var result = await surface.BootAsync("ios:A1B2");

        Assert.False(result.Success);
        Assert.Contains("control token", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedProtocolMajor_ReportsIncompatible_WithoutCallingTheHost()
    {
        using var surface = CreateSurface(schemaVersion: "2.0");

        var health = await surface.GetHealthAsync();

        Assert.Equal(DeviceHostAvailability.Incompatible, health.Availability);
        Assert.Contains("2.0", health.Reason);

        // Refusing before making a request is deliberate: driving a protocol we do not understand
        // risks silently wrong device control rather than a clean failure.
        lock (_requests)
            Assert.DoesNotContain(_requests, r => r.Path == "/api/v1/status");
    }

    [Fact]
    public async Task MissingProtocolVersion_IsReportedIncompatible()
    {
        using var surface = CreateSurface(schemaVersion: null);

        var health = await surface.GetHealthAsync();

        Assert.Equal(DeviceHostAvailability.Incompatible, health.Availability);
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.4", false)]
    [InlineData("1", false)]
    [InlineData("2.0", false)]
    [InlineData("0.9", false)]
    [InlineData("nonsense", false)]
    public void ProtocolSupport_TracksTheMajorVersion(string reported, bool supported)
    {
        Assert.Equal(supported, MobileCanvasProtocol.IsSupported(reported));
    }

    [Theory]
    [InlineData("1.0", "0.1.16", true)]
    [InlineData("1.0", "0.1.17", true)]
    [InlineData("1.0", "0.1.16-rc.1", false)]
    [InlineData("1.0", "0.1.15", false)]
    [InlineData("1.1", "0.1.16", false)]
    [InlineData(null, "0.1.16", false)]
    public void HostCompatibility_RequiresTheExactProtocolAndMinimumVersion(
        string? protocol,
        string? version,
        bool compatible)
    {
        Assert.Equal(compatible, MobileCanvasProtocol.IsHostCompatible(protocol, version));
    }
}
