using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Access control on the device video proxy.
/// <para>
/// A WebSocket is not subject to the same-origin policy, so any page a user visits can open one to
/// localhost. Without both gates below, a hostile page could receive a live feed of the user's
/// device screen — with the broker attaching the device host's bearer token on its behalf. A
/// review found exactly that hole, so it is pinned here.
/// </para>
/// </summary>
public class DeviceVideoProxyTests : IAsyncLifetime
{
    private const string DeviceId = "android:android-emulator:pixel";

    private sealed record SocketMessage(byte[] Payload, WebSocketMessageType Type);

    private sealed class VideoSurface : IDeviceSurface
    {
        public DeviceTarget? Device { get; set; } = Target(liveStream: true);

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });

        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>(Device is null ? [] : [Device]);

        public Task<DeviceTarget?> GetAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                string.Equals(deviceId, Device?.Id, StringComparison.OrdinalIgnoreCase)
                    ? Device
                    : null);

        public Task<DeviceOperationResult> BootAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeviceOperationResult.Ok());

        public Task<DeviceOperationResult> ShutdownAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeviceOperationResult.Ok());

        public Task<DeviceOperationResult> TapAsync(
            string deviceId,
            DevicePoint point,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DeviceOperationResult.Ok());

        public Task<byte[]?> ScreenshotAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        private static DeviceTarget Target(bool liveStream) => new()
        {
            Id = DeviceId,
            Platform = DevicePlatforms.Android,
            NativeId = "emulator-5554",
            Name = "Pixel",
            State = DeviceStates.Booted,
            Capabilities = new DeviceCapabilities { LiveStream = liveStream },
        };

        public void SetLiveStream(bool enabled) => Device = Target(enabled);
    }

    private sealed class ScriptedWebSocket(params SocketMessage[] messages) : WebSocket
    {
        private readonly Queue<SocketMessage> _messages = new(messages);
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;

        public bool Aborted { get; private set; }
        public bool BlockWhenEmpty { get; init; }
        public int CloseCalls { get; private set; }
        public List<SocketMessage> Sent { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;
        public override string? CloseStatusDescription => _closeStatusDescription;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            Aborted = true;
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            CloseCalls++;
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose() => _state = WebSocketState.Closed;

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (_messages.Count == 0)
            {
                if (BlockWhenEmpty)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

                return new WebSocketReceiveResult(
                    0,
                    WebSocketMessageType.Close,
                    true,
                    WebSocketCloseStatus.NormalClosure,
                    "done");
            }

            var message = _messages.Dequeue();
            message.Payload.CopyTo(buffer.Array!, buffer.Offset);
            return new WebSocketReceiveResult(
                message.Payload.Length,
                message.Type,
                true);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Sent.Add(new SocketMessage(buffer.ToArray(), messageType));
            return Task.CompletedTask;
        }
    }

    private BrokerServer _broker = null!;
    private CancellationTokenSource _cts = null!;
    private VideoSurface _surface = null!;
    private MobileCanvasHostState? _hostState;
    private int _port;

    public async Task InitializeAsync()
    {
        _port = GetFreePort();
        _surface = new VideoSurface();
        _broker = new BrokerServer(
            _port,
            new DeviceRegistry(_surface),
            () => _hostState);
        _cts = new CancellationTokenSource();
        _ = _broker.RunAsync(_cts.Token);
        await WaitForBrokerAsync(_port);
    }

    public Task DisposeAsync()
    {
        _cts.Cancel();
        _broker.Dispose();
        return Task.CompletedTask;
    }

    private static int GetFreePort() => TestPorts.Reserve();

    private string EmbedToken => (string)typeof(BrokerServer)
        .GetField("_embedToken", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(_broker)!;

    private static MobileCanvasHostState Host(
        string schemaVersion = "1.0",
        MobileCanvasHostStateOrigin origin = MobileCanvasHostStateOrigin.ProtocolScoped) =>
        new()
        {
            SchemaVersion = schemaVersion,
            Version = MobileCanvasProtocol.ValidatedHostVersion,
            Port = 1,
            ProcessId = 42,
            ControlToken = "host-token",
            Origin = origin,
        };

    private static async Task WaitForBrokerAsync(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var i = 0; i < 50; i++)
        {
            try
            {
                var response = await http.GetAsync($"http://localhost:{port}/api/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch { }
            await Task.Delay(100);
        }
    }

    private async Task<WebSocketException?> ConnectAsync(string query, string? origin)
    {
        using var socket = new ClientWebSocket();
        if (origin is not null)
            socket.Options.SetRequestHeader("Origin", origin);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await socket.ConnectAsync(new Uri($"ws://localhost:{_port}/ws/video?{query}"), timeout.Token);
            return null;
        }
        catch (WebSocketException ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task Pump_ForwardsTheDescriptorAndUsesBoundedAbortTeardown()
    {
        var descriptor = Encoding.UTF8.GetBytes(
            """{"encoding":"h264-annexb","framesPerSecond":30,"scale":1,"source":"framebuffer"}""");
        var upstream = new ScriptedWebSocket(
            new SocketMessage(descriptor, WebSocketMessageType.Text));
        var downstream = new ScriptedWebSocket { BlockWhenEmpty = true };

        await BrokerServer.PumpVideoAsync(upstream, downstream);

        var forwarded = Assert.Single(downstream.Sent);
        Assert.Equal(WebSocketMessageType.Text, forwarded.Type);
        Assert.Equal(descriptor, forwarded.Payload);
        Assert.True(upstream.Aborted);
        Assert.True(downstream.Aborted);
        Assert.Equal(0, upstream.CloseCalls);
        Assert.Equal(0, downstream.CloseCalls);
    }

    [Fact]
    public async Task Pump_StopsWhenTheBrowserDisconnectsDuringAnIdleUpstream()
    {
        var upstream = new ScriptedWebSocket { BlockWhenEmpty = true };
        var downstream = new ScriptedWebSocket();

        await BrokerServer.PumpVideoAsync(upstream, downstream).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(upstream.Aborted);
        Assert.True(downstream.Aborted);
    }

    [Fact]
    public async Task RejectsAForeignOrigin()
    {
        // The critical case: a page on the open internet must not be able to open this socket.
        var error = await ConnectAsync("deviceId=emulator-5554", origin: "https://evil.example");

        Assert.NotNull(error);
        Assert.Contains("403", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsALocalPageWithoutTheEmbedToken()
    {
        // A loopback origin proves the caller is local, not that it is an Inspector session. Any
        // other local page — a dev server, a docs site on localhost — must still be refused.
        var error = await ConnectAsync("deviceId=emulator-5554", origin: $"http://localhost:{_port}");

        Assert.NotNull(error);
        Assert.Contains("403", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsAWrongEmbedToken()
    {
        var error = await ConnectAsync(
            "deviceId=emulator-5554&embed=not-the-token",
            origin: $"http://localhost:{_port}");

        Assert.NotNull(error);
        Assert.Contains("403", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsARequestWithNoOriginAtAll()
    {
        // A non-browser client has no business on the video channel; it has the CLI and MCP.
        var error = await ConnectAsync("deviceId=emulator-5554&embed=x", origin: null);

        Assert.NotNull(error);
    }

    [Fact]
    public async Task RejectsAnIncompatibleHostBeforeOpeningTheUpstreamSocket()
    {
        _hostState = Host(schemaVersion: "2.0");

        var error = await ConnectAsync(
            $"deviceId={Uri.EscapeDataString(DeviceId)}&embed={EmbedToken}",
            origin: $"http://localhost:{_port}");

        Assert.NotNull(error);
        Assert.Contains("503", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsALegacyHostBeforeForwardingItsCredential()
    {
        _hostState = Host(origin: MobileCanvasHostStateOrigin.Legacy);

        var error = await ConnectAsync(
            $"deviceId={Uri.EscapeDataString(DeviceId)}&embed={EmbedToken}",
            origin: $"http://localhost:{_port}");

        Assert.NotNull(error);
        Assert.Contains("503", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsADeviceThatDoesNotAdvertiseLiveStreaming()
    {
        _hostState = Host();
        _surface.SetLiveStream(false);

        var error = await ConnectAsync(
            $"deviceId={Uri.EscapeDataString(DeviceId)}&embed={EmbedToken}",
            origin: $"http://localhost:{_port}");

        Assert.NotNull(error);
        Assert.Contains("503", error!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
