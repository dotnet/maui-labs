using System.Collections.Concurrent;
using System.Text;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MobileCanvasMcpProxyTests
{
    private sealed class OneDeviceSurface : IDeviceSurface
    {
        private readonly DeviceTarget _device = new()
        {
            Id = "ios:simulator:A1B2",
            Platform = DevicePlatforms.Ios,
            NativeId = "A1B2",
            Udid = "A1B2",
            Name = "iPhone 16",
            State = DeviceStates.Booted,
        };

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([_device]);
        public Task<DeviceTarget?> GetAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceTarget?>(_device);
        public Task<DeviceOperationResult> BootAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> ShutdownAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> TapAsync(string deviceId, DevicePoint point, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeviceOperationResult.Ok());
        public Task<byte[]?> ScreenshotAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
    }

    private sealed class RecordingLeaseClient : IDeviceMutationLeaseClient
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public ConcurrentQueue<string> ClaimLeaseIds { get; } = new();
        public TaskCompletionSource Attempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Begun { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Allow { get; set; } = true;
        public bool FailHeartbeat { get; set; }

        public Task<DeviceMutationLeaseResult> ControlAsync(
            string action,
            string leaseId,
            string? transactionId,
            string? deviceId,
            bool catalog,
            CancellationToken cancellationToken)
        {
            Calls.Enqueue($"{action}:{deviceId}:{catalog}:{transactionId}");
            if (action == "claim")
                ClaimLeaseIds.Enqueue(leaseId);
            Attempted.TrySetResult();
            if (action == "begin")
                Begun.TrySetResult();
            var allowed = Allow && !(FailHeartbeat && action == "heartbeat");
            return Task.FromResult(new DeviceMutationLeaseResult
            {
                Ok = true,
                Allowed = allowed && action != "release",
                YouHold = allowed && action != "release",
                HeldByOther = !allowed,
                TransactionId = action is "begin" or "heartbeat" ? transactionId : null,
                HolderKind = Allow ? "mobile-canvas-mcp" : "inspector",
                Label = Allow ? "companion" : "Inspector",
            });
        }
    }

    private sealed class GatedReadStream(byte[] content, Task gate) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            gate.GetAwaiter().GetResult();
            return _inner.Read(buffer, offset, count);
        }
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            return await _inner.ReadAsync(buffer, cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void Policy_PinsReadOnlyToolsAndDefaultsUnknownToolsToMutating()
    {
        Assert.True(MobileCanvasToolMutationPolicy.IsReadOnly("mobile_device_list", advertisedReadOnly: true));
        Assert.False(MobileCanvasToolMutationPolicy.IsReadOnly("mobile_device_tap", advertisedReadOnly: false));
        Assert.False(MobileCanvasToolMutationPolicy.IsReadOnly("mobile_device_future_action", advertisedReadOnly: true));
        Assert.True(MobileCanvasToolMutationPolicy.UsesCatalogLease("mobile_device_create"));
    }

    [Fact]
    public async Task MutatingCall_ClaimsBeforeForwardAndReleasesBeforeResponse()
    {
        var leases = new RecordingLeaseClient();
        var request = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2","x":1,"y":2}}}
            """;
        var response = """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"ok"}]}}""";
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream(
            Encoding.UTF8.GetBytes(response + "\n"),
            leases.Begun.Task);
        var terminated = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => terminated = true,
            timeout.Token);

        Assert.False(terminated);
        Assert.Contains("\"mobile_device_tap\"", Read(childInput));
        Assert.Contains("\"text\":\"ok\"", Read(clientOutput));
        Assert.Equal(
            ["claim", "begin", "end", "release"],
            leases.Calls.Select(call => call.Split(':')[0]).ToArray());
    }

    [Fact]
    public async Task LeaseConflict_ReturnsToolErrorWithoutForwardingMutation()
    {
        var leases = new RecordingLeaseClient { Allow = false };
        var request = """
            {"jsonrpc":"2.0","id":"call-1","method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2","x":1,"y":2}}}
            """;
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream([], leases.Attempted.Task);

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            CancellationToken.None);

        Assert.Empty(childInput.ToArray());
        Assert.Contains("\"isError\":true", Read(clientOutput));
        Assert.Contains("Another DevFlow session", Read(clientOutput));
    }

    [Fact]
    public async Task ReadOnlyCall_ForwardsWithoutTakingADeviceLease()
    {
        var leases = new RecordingLeaseClient();
        var request = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_list","arguments":{}}}
            """;
        var response = """{"jsonrpc":"2.0","id":1,"result":{"content":[]}}""";
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream(
            Encoding.UTF8.GetBytes(response + "\n"),
            Task.Delay(50));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            CancellationToken.None);

        Assert.Empty(leases.Calls);
        Assert.Contains("\"mobile_device_list\"", Read(childInput));
        Assert.Contains("\"content\":[]", Read(clientOutput));
    }

    [Fact]
    public async Task UnknownCompletion_LeavesTheBrokerTransactionToExpire()
    {
        var leases = new RecordingLeaseClient();
        var request = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2","x":1,"y":2}}}
            """;
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream([], leases.Begun.Task);

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            CancellationToken.None);

        Assert.Equal(
            ["claim", "begin"],
            leases.Calls.Select(call => call.Split(':')[0]).ToArray());
    }

    [Fact]
    public async Task LostHeartbeat_TerminatesTheChildAndLeavesTheTransactionToExpire()
    {
        var leases = new RecordingLeaseClient { FailHeartbeat = true };
        var request = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2","x":1,"y":2}}}
            """;
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream(
            [],
            Task.Delay(Timeout.InfiniteTimeSpan));
        var terminated = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MobileCanvasMcpProxy(leases, TimeSpan.FromMilliseconds(5)).RunAsync(
                clientInput,
                clientOutput,
                childInput,
                childOutput,
                () => terminated = true,
                timeout.Token));

        Assert.True(terminated);
        Assert.Equal(
            ["claim", "begin", "heartbeat"],
            leases.Calls.Select(call => call.Split(':')[0]).ToArray());
    }

    [Fact]
    public async Task DuplicateReadOnlyId_CannotReleaseAnActiveMutation()
    {
        var leases = new RecordingLeaseClient();
        var requests = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2","x":1,"y":2}}}
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_list","arguments":{}}}
            """;
        var response = """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"mutation complete"}]}}""";
        await using var clientInput = Bytes(requests + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream(
            Encoding.UTF8.GetBytes(response + "\n"),
            Task.Delay(100));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            CancellationToken.None);

        var forwarded = Read(childInput);
        Assert.Contains("mobile_device_tap", forwarded);
        Assert.DoesNotContain("mobile_device_list", forwarded);
        Assert.Contains("already active", Read(clientOutput));
        Assert.Equal(
            ["claim", "begin", "end", "release"],
            leases.Calls.Select(call => call.Split(':')[0]).ToArray());
    }

    [Fact]
    public async Task MutatingBatch_IsRejectedBeforeAnyLeaseOrForwarding()
    {
        var leases = new RecordingLeaseClient();
        var request = """
            [{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2"}}}]
            """;
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream([], Task.Delay(100));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            CancellationToken.None);

        Assert.Empty(leases.Calls);
        Assert.Empty(childInput.ToArray());
        Assert.Contains("Batched mutating", Read(clientOutput));
    }

    [Fact]
    public async Task ConcurrentMutatingCalls_UseOperationScopedLeaseIdentities()
    {
        var leases = new RecordingLeaseClient();
        var requests = """
            {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2","x":1,"y":2}}}
            {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2","x":3,"y":4}}}
            """;
        var responses = """
            {"jsonrpc":"2.0","id":1,"result":{"content":[]}}
            {"jsonrpc":"2.0","id":2,"result":{"content":[]}}
            """;
        await using var clientInput = Bytes(requests + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream(
            Encoding.UTF8.GetBytes(responses + "\n"),
            Task.Delay(100));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            CancellationToken.None);

        Assert.Equal(2, leases.ClaimLeaseIds.Count);
        Assert.Equal(2, leases.ClaimLeaseIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The client side of this proxy is an untrusted model-driven process. Every field it sends can
    /// be the wrong JSON type, and each of those has to become a bounded JSON-RPC error rather than
    /// an exception that kills the pump — a dead pump stops arbitrating leases while the companion
    /// child process is still alive and still able to mutate devices.
    /// </summary>
    [Theory]
    // method is not a string
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":7}""", "The JSON-RPC method must be a string")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":{"tools":"call"}}""", "The JSON-RPC method must be a string")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":["tools/call"]}""", "The JSON-RPC method must be a string")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":true}""", "The JSON-RPC method must be a string")]
    // params is not an object
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":"mobile_device_tap"}""", "The JSON-RPC params member must be an object")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":[{"name":"mobile_device_tap"}]}""", "The JSON-RPC params member must be an object")]
    // params.name is not a string
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":42}}""", "The tools/call params.name must be a string")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":{"$ref":"x"}}}""", "The tools/call params.name must be a string")]
    // params.arguments is not an object
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":"ios:simulator:A1B2"}}""", "The tools/call params.arguments must be an object")]
    // arguments.deviceId is not a string
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":99}}}""", "string arguments.deviceId")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":{"id":"x"}}}}""", "string arguments.deviceId")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":["a"]}}}""", "string arguments.deviceId")]
    public async Task HostileTypedRequest_IsAnswered_WithoutLeaseOrForwarding(
        string request,
        string expected)
    {
        var leases = new RecordingLeaseClient();
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream([], Task.Delay(50));
        var terminated = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => terminated = true,
            timeout.Token);

        Assert.False(terminated);
        Assert.Empty(leases.Calls);
        Assert.Empty(childInput.ToArray());
        Assert.Contains(expected, Read(clientOutput), StringComparison.Ordinal);
    }

    /// <summary>
    /// A batch whose members carry non-string methods or tool names cannot be classified, so it is
    /// refused wholesale rather than forwarded on the assumption it is read-only.
    /// </summary>
    [Theory]
    [InlineData("""[{"jsonrpc":"2.0","id":1,"method":5}]""")]
    [InlineData("""[{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":5}}]""")]
    [InlineData("""[{"jsonrpc":"2.0","id":1,"method":"tools/call","params":"list"}]""")]
    public async Task HostileTypedBatch_IsRefusedBeforeForwarding(string request)
    {
        var leases = new RecordingLeaseClient();
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream([], Task.Delay(50));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            timeout.Token);

        Assert.Empty(leases.Calls);
        Assert.Empty(childInput.ToArray());
        Assert.Contains("Batched mutating", Read(clientOutput), StringComparison.Ordinal);
    }

    /// <summary>
    /// A JSON-RPC id may only be a string or a number. An object or array in that position is
    /// never echoed back, so a client cannot dictate the shape of a message the proxy emits.
    /// </summary>
    [Fact]
    public async Task NonScalarRequestId_IsNeverEchoedIntoTheErrorResponse()
    {
        var leases = new RecordingLeaseClient();
        var request = """
            {"jsonrpc":"2.0","id":{"forged":"id"},"method":9}
            """;
        await using var clientInput = Bytes(request + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream([], Task.Delay(50));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            timeout.Token);

        var written = Read(clientOutput);
        Assert.Contains("\"id\":null", written, StringComparison.Ordinal);
        Assert.DoesNotContain("forged", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The child process is untrusted too: it is a separately shipped binary. A tools/list answer
    /// with wrong-typed names or annotations must not throw, and a non-boolean readOnlyHint is not
    /// evidence of anything, so the tool stays classified as mutating.
    /// </summary>
    [Fact]
    public async Task HostileToolInventory_IsIgnoredWithoutWideningTheReadOnlySet()
    {
        var leases = new RecordingLeaseClient();
        var requests = """
            {"jsonrpc":"2.0","id":1,"method":"tools/list"}
            {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"mobile_device_tap","arguments":{"deviceId":"ios:simulator:A1B2"}}}
            """;
        var responses = """
            {"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":17,"annotations":{"readOnlyHint":true}},{"name":"mobile_device_tap","annotations":{"readOnlyHint":"yes"}},{"name":"mobile_device_erase","annotations":"read-only"}]}}
            {"jsonrpc":"2.0","id":2,"result":{"content":[]}}
            """;
        await using var clientInput = Bytes(requests + "\n");
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new GatedReadStream(
            Encoding.UTF8.GetBytes(responses + "\n"),
            Task.Delay(100));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await new MobileCanvasMcpProxy(leases).RunAsync(
            clientInput,
            clientOutput,
            childInput,
            childOutput,
            () => { },
            timeout.Token);

        // The mutation was still leased: a string readOnlyHint never promoted the tap to read-only.
        Assert.Equal(
            ["claim", "begin", "end", "release"],
            leases.Calls.Select(call => call.Split(':')[0]).ToArray());
    }

    /// <summary>
    /// A fault in the child pump used to be swallowed by <c>Task.WhenAny</c>: the proxy returned as
    /// though the client had simply closed stdin. It now surfaces, and both pumps are observed.
    /// </summary>
    [Fact]
    public async Task ChildPumpFailure_SurfacesInsteadOfLookingLikeACleanShutdown()
    {
        var leases = new RecordingLeaseClient();
        await using var clientInput = new GatedReadStream([], Task.Delay(Timeout.InfiniteTimeSpan));
        await using var clientOutput = new MemoryStream();
        await using var childInput = new MemoryStream();
        await using var childOutput = new ThrowingReadStream();
        var terminated = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<IOException>(() =>
            new MobileCanvasMcpProxy(leases).RunAsync(
                clientInput,
                clientOutput,
                childInput,
                childOutput,
                () => terminated = true,
                timeout.Token));

        Assert.True(terminated);
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("The companion stdout pipe broke.");
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("The companion stdout pipe broke."));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task BrokerDeviceLeaseRoute_SerializesCompanionMutations()
    {
        var port = TestPorts.Reserve();
        using var broker = new BrokerServer(port, new DeviceRegistry(new OneDeviceSurface()));
        using var cancellation = new CancellationTokenSource();
        _ = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(port);

        var first = await BrokerClient.ControlDeviceMutationLeaseAsync(
            port,
            "claim",
            "lease-one",
            deviceId: "ios:simulator:A1B2");
        var begun = await BrokerClient.ControlDeviceMutationLeaseAsync(
            port,
            "begin",
            "lease-one",
            transactionId: "transaction-one",
            deviceId: "ios:simulator:A1B2");
        var blocked = await BrokerClient.ControlDeviceMutationLeaseAsync(
            port,
            "claim",
            "lease-two",
            deviceId: "ios:simulator:A1B2");

        Assert.True(first.Allowed, first.Error);
        Assert.Equal("transaction-one", begun.TransactionId);
        Assert.True(blocked.HeldByOther);

        await BrokerClient.ControlDeviceMutationLeaseAsync(
            port,
            "end",
            "lease-one",
            transactionId: "transaction-one",
            deviceId: "ios:simulator:A1B2");
        await BrokerClient.ControlDeviceMutationLeaseAsync(
            port,
            "release",
            "lease-one",
            deviceId: "ios:simulator:A1B2");
        var second = await BrokerClient.ControlDeviceMutationLeaseAsync(
            port,
            "claim",
            "lease-two",
            deviceId: "ios:simulator:A1B2");

        Assert.True(second.Allowed, second.Error);
        cancellation.Cancel();
    }

    private static MemoryStream Bytes(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static string Read(MemoryStream stream)
    {
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task WaitForBrokerAsync(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var response = await http.GetAsync($"http://localhost:{port}/api/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(50);
        }
        Assert.Fail("Broker did not start.");
    }
}
