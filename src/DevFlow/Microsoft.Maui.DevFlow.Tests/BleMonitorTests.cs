using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class BleMonitorTests
{
    [Fact]
    public void GetEvents_NegativeLimit_ReturnsEmpty()
    {
        var monitor = new BleMonitor();
        monitor.RecordScanResult("device-1", "Device 1", -42);

        var events = monitor.GetEvents(-1);

        Assert.Empty(events);
    }

    [Fact]
    public void GetEvents_FiltersByTypeAndAppliesLimit()
    {
        var monitor = new BleMonitor();
        monitor.RecordScanResult("scan-1", "Scan 1", -70);
        monitor.RecordConnectionStateChanged("connected-1", "Connected 1", "connected");
        monitor.RecordConnectionStateChanged("connected-2", "Connected 2", "connected");

        var events = monitor.GetEvents(1, "connected");

        var evt = Assert.Single(events);
        Assert.Equal("connected", evt.Type);
        Assert.Equal("connected-2", evt.DeviceId);
    }

    [Fact]
    public void RecordEvent_WhenBufferExceedsCapacity_TrimsOldEventsAndReportsBufferedCount()
    {
        var monitor = new BleMonitor(maxEvents: 2);
        monitor.RecordScanResult("device-1", "Device 1", -71);
        monitor.RecordScanResult("device-2", "Device 2", -72);
        monitor.RecordScanResult("device-3", "Device 3", -73);

        var events = monitor.GetEvents(10);
        var status = JsonSerializer.SerializeToElement(monitor.GetStatus());

        Assert.Equal(new[] { "device-2", "device-3" }, events.Select(e => e.DeviceId));
        Assert.Equal(2, status.GetProperty("eventCount").GetInt32());
        Assert.False(status.GetProperty("supportsScanning").GetBoolean());
    }

    [Fact]
    public void Dispose_CallsPlatformDisposeHook()
    {
        var monitor = new DisposableBleMonitor();

        monitor.Dispose();

        Assert.True(monitor.PlatformDisposed);
    }

    [Fact]
    public void RecordEvent_AfterDispose_IgnoresEvent()
    {
        var monitor = new BleMonitor();

        monitor.Dispose();
        monitor.RecordScanResult("device-1", "Device 1", -42);

        Assert.Empty(monitor.GetEvents());
        Assert.Equal("BLE monitor disposed", monitor.StartScanning());
    }

    [Fact]
    public void Subscribe_WithTypeFilter_StreamsMatchingEventsWithoutConsumingBuffer()
    {
        var monitor = new BleMonitor();
        var queue = monitor.Subscribe("connected");

        monitor.RecordScanResult("scan-1", "Scan 1", -70);
        monitor.RecordConnectionStateChanged("connected-1", "Connected 1", "connected");

        var message = Assert.Single(queue);
        using var document = JsonDocument.Parse(message);
        var evt = document.RootElement.GetProperty("event");
        Assert.Equal("connected", evt.GetProperty("type").GetString());
        Assert.Equal("connected-1", evt.GetProperty("deviceId").GetString());

        var buffered = monitor.GetEvents(10);
        Assert.Equal(new[] { "scan-1", "connected-1" }, buffered.Select(e => e.DeviceId));
    }

    [Fact]
    public void TryStartScanning_WhenAlreadyScanning_DoesNotClaimScanOwnership()
    {
        var monitor = new ScanningBleMonitor();

        Assert.True(monitor.TryStartScanning(out var firstError));
        Assert.Null(firstError);
        Assert.False(monitor.TryStartScanning(out var secondError));
        Assert.Null(secondError);
        Assert.Equal(1, monitor.StartCount);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-an-int")]
    public async Task HandleBleEvents_InvalidLimit_ReturnsBadRequest(string limit)
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });
        var request = new HttpRequest
        {
            QueryParams = new Dictionary<string, string> { ["limit"] = limit }
        };

        var response = await InvokeHandlerAsync(service, "HandleBleEvents", request);

        Assert.Equal(400, response.StatusCode);
        Assert.NotNull(response.Body);

        var json = JsonDocument.Parse(response.Body!).RootElement;
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_limit", json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task HandleBleEvents_ValidLimit_ReturnsFilteredEvents()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });
        service.Ble.RecordScanResult("scan-1", "Scan 1", -80);
        service.Ble.RecordConnectionStateChanged("connected-1", "Connected 1", "connected");
        service.Ble.RecordConnectionStateChanged("connected-2", "Connected 2", "connected");

        var request = new HttpRequest
        {
            QueryParams = new Dictionary<string, string>
            {
                ["limit"] = "1",
                ["type"] = "connected"
            }
        };

        var response = await InvokeHandlerAsync(service, "HandleBleEvents", request);

        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Body);

        var json = JsonDocument.Parse(response.Body!).RootElement;
        Assert.Equal(1, json.GetProperty("count").GetInt32());
        var evt = Assert.Single(json.GetProperty("events").EnumerateArray());
        Assert.Equal("connected", evt.GetProperty("type").GetString());
        Assert.Equal("connected-2", evt.GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task HandleCapabilities_BaseBleMonitor_DoesNotAdvertiseScan()
    {
        using var service = new DevFlowAgentService(new AgentOptions { Enabled = false });

        var response = await InvokeHandlerAsync(service, "HandleCapabilities", new HttpRequest());

        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Body);

        var json = JsonDocument.Parse(response.Body!).RootElement;
        var ble = json.GetProperty("ble");
        var features = ble.GetProperty("features").EnumerateArray()
            .Select(feature => feature.GetString())
            .ToArray();

        Assert.True(ble.GetProperty("supported").GetBoolean());
        Assert.Contains("status", features);
        Assert.Contains("events", features);
        Assert.Contains("stream", features);
        Assert.DoesNotContain("scan", features);
    }

    [Fact]
    public async Task BleWebSocket_ReplaysBufferedEventsAndStreamsLiveFilteredEvents()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        service.Ble.RecordScanResult("scan-buffered", "Scan Buffered", -80);
        service.Ble.RecordConnectionStateChanged("connected-buffered", "Connected Buffered", "connected");
        service.StartServerOnly(new ImmediateDispatcher());

        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws/v1/ble?replay=10&type=connected"), cts.Token);

        var subscribed = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("subscribed", subscribed.GetProperty("type").GetString());
        Assert.Equal(10, subscribed.GetProperty("replay").GetInt32());
        Assert.Equal("connected", subscribed.GetProperty("eventType").GetString());

        var replay = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("replay", replay.GetProperty("type").GetString());
        Assert.Equal(1, replay.GetProperty("count").GetInt32());
        var replayedEvent = Assert.Single(replay.GetProperty("events").EnumerateArray());
        Assert.Equal("connected-buffered", replayedEvent.GetProperty("deviceId").GetString());

        service.Ble.RecordScanResult("scan-live", "Scan Live", -81);
        service.Ble.RecordConnectionStateChanged("connected-live", "Connected Live", "connected");

        var live = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("ble_event", live.GetProperty("type").GetString());
        Assert.Equal("connected-live", live.GetProperty("event").GetProperty("deviceId").GetString());

        var bufferedIds = service.Ble.GetEvents(10).Select(e => e.DeviceId ?? "").ToArray();
        Assert.Equal(["scan-buffered", "connected-buffered", "scan-live", "connected-live"], bufferedIds);
    }

    [Fact]
    public async Task BleWebSocket_ReplayZeroSkipsReplayAndStreamsLiveEvents()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        service.Ble.RecordScanResult("scan-buffered", "Scan Buffered", -80);
        service.StartServerOnly(new ImmediateDispatcher());

        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws/v1/ble?replay=0"), cts.Token);

        var subscribed = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("subscribed", subscribed.GetProperty("type").GetString());
        Assert.Equal(0, subscribed.GetProperty("replay").GetInt32());

        service.Ble.RecordScanResult("scan-live", "Scan Live", -81);

        var live = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("ble_event", live.GetProperty("type").GetString());
        Assert.Equal("scan-live", live.GetProperty("event").GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task BleWebSocket_InvalidReplaySendsError()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        service.StartServerOnly(new ImmediateDispatcher());

        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws/v1/ble?replay=-1"), cts.Token);

        var error = await ReceiveJsonAsync(ws, cts.Token);
        Assert.Equal("error", error.GetProperty("type").GetString());
        Assert.Contains("replay", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task<HttpResponse> InvokeHandlerAsync(DevFlowAgentService service, string methodName, HttpRequest request)
    {
        var method = typeof(DevFlowAgentService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<HttpResponse>)method.Invoke(service, [request])!;
    }

    private static async Task<JsonElement> ReceiveJsonAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("WebSocket closed before a text message was received.");

            stream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var json = Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class DisposableBleMonitor : BleMonitor
    {
        public bool PlatformDisposed { get; private set; }

        protected override void DisposePlatform()
        {
            PlatformDisposed = true;
        }
    }

    private sealed class ScanningBleMonitor : BleMonitor
    {
        public int StartCount { get; private set; }

        protected override string? StartPlatformScan()
        {
            StartCount++;
            return null;
        }
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            action();
            return true;
        }

        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class ImmediateDispatcherTimer : IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick
        {
            add { }
            remove { }
        }

        public void Start()
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}
