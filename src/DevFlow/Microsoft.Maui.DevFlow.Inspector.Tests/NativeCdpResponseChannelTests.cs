using System.Text.Json;
using Microsoft.Maui.DevFlow.WebView;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

public class NativeCdpResponseChannelTests
{
    [Fact]
    public async Task Handshake_OnlyItsActualNonceCompletesProbe_NotCommandOrOtherChannel()
    {
        using var channel = new NativeCdpResponseChannel();
        using var other = new NativeCdpResponseChannel();
        var handshake = channel.BeginHandshake();
        var command = channel.Register(42);
        var payload = HandshakePayload(handshake.Script);

        Assert.False(other.TryReceive(payload));
        Assert.False(channel.TryReceive(JsonSerializer.Serialize(new { channel = channel.BindingName, kind = "handshake", nonce = "wrong" })));
        using var message = JsonDocument.Parse(payload);
        Assert.False(channel.TryReceive(JsonSerializer.Serialize(new
        {
            channel = channel.BindingName, kind = "response", nonce = message.RootElement.GetProperty("nonce").GetString()
        })));
        Assert.False(handshake.Received.IsCompleted);
        Assert.False(channel.TryReceive(Payload(channel, """{"id":41,"result":{}}""")));
        Assert.True(channel.TryReceive(payload));
        await handshake.Received;
        Assert.False(command.IsCompleted);
        Assert.False(channel.TryReceive(payload));
        Assert.True(channel.TryReceive(Payload(channel, """{"id":42,"result":{}}""")));
        Assert.Contains("\"id\":42", await command);
        channel.EndHandshake(handshake.Received);
        Assert.False(channel.TryReceive(payload));
        channel.Remove(42);
    }

    [Fact]
    public async Task Handshake_RetriedInitializationRejectsOldNonce_AndOldCleanupCannotCancelReplacement()
    {
        using var channel = new NativeCdpResponseChannel();
        var old = channel.BeginHandshake();
        var current = channel.BeginHandshake();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Received);
        channel.EndHandshake(old.Received);
        Assert.False(channel.TryReceive(HandshakePayload(old.Script)));
        Assert.False(current.Received.IsCompleted);
        Assert.True(channel.TryReceive(HandshakePayload(current.Script)));
        await current.Received;
        channel.EndHandshake(current.Received);
    }

    [Fact]
    public async Task Handshake_CancellationAndDisposalRejectLateCallbacks_WithoutCompletingPendingCommand()
    {
        using var channel = new NativeCdpResponseChannel();
        var pending = channel.Register(42);
        var canceled = channel.BeginHandshake();
        channel.EndHandshake(canceled.Received);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.Received);
        Assert.False(channel.TryReceive(HandshakePayload(canceled.Script)));
        Assert.False(pending.IsCompleted);
        var active = channel.BeginHandshake();
        Assert.True(channel.TryReceive(Payload(channel, """{"id":42,"result":{}}""")));
        await pending;
        Assert.False(active.Received.IsCompleted);
        var next = channel.Register(43);
        channel.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active.Received);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
        Assert.False(channel.TryReceive(HandshakePayload(active.Script)));
        Assert.Throws<ObjectDisposedException>(() => channel.BeginHandshake());
    }

    [Fact]
    public async Task Receive_RegisteredBeforeDispatch_PreservesActualResultAndCompletesOnlyOnce()
    {
        using var channel = new NativeCdpResponseChannel();
        var pending = channel.Register(42);
        const string response = """{"id":42,"result":{"result":{"value":"actual result"}}}""";

        Assert.True(channel.TryReceive(Payload(channel, response)));
        Assert.False(channel.TryReceive(Payload(channel, """{"id":42,"result":{"value":"duplicate"}}""")));
        Assert.Equal(response, await pending);
        channel.Remove(42);
        Assert.False(channel.TryReceive(Payload(channel, response)));
    }

    [Fact]
    public async Task Receive_LateOldReply_CannotCompleteNextWireId()
    {
        using var channel = new NativeCdpResponseChannel();
        var old = channel.Register(1);
        channel.Remove(1);
        var current = channel.Register(2);

        Assert.False(channel.TryReceive(Payload(channel, """{"id":1,"result":{}}""")));
        Assert.False(current.IsCompleted);
        Assert.True(channel.TryReceive(Payload(channel, """{"id":2,"result":{"value":"current"}}""")));
        Assert.Contains("current", await current);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
    }

    [Fact]
    public async Task Receive_SameWireIdOnDifferentAttachment_RequiresItsUniqueChannel()
    {
        using var first = new NativeCdpResponseChannel();
        using var second = new NativeCdpResponseChannel();
        var firstPending = first.Register(7);
        var secondPending = second.Register(7);
        const string response = """{"id":7,"result":{}}""";
        Assert.NotEqual(first.BindingName, second.BindingName);

        Assert.False(second.TryReceive(Payload(first, response)));
        Assert.False(secondPending.IsCompleted);
        Assert.True(first.TryReceive(Payload(first, response)));
        Assert.Equal(response, await firstPending);
        Assert.True(second.TryReceive(Payload(second, response)));
        Assert.Equal(response, await secondPending);
    }

    [Fact]
    public async Task Dispose_PendingResponses_AreCanceledAndLateMessagesCannotRestoreThem()
    {
        var channel = new NativeCdpResponseChannel();
        var first = channel.Register(1);
        var second = channel.Register(2);
        channel.Dispose();
        channel.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(channel.TryReceive(Payload(channel, """{"id":1,"result":{}}""")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.Register(3));
    }

    [Theory]
    [InlineData("""{"id":12,"error":{"code":-32601,"message":"Unsupported method"}}""")]
    [InlineData("""{"id":12,"result":{"exceptionDetails":{"text":"ReferenceError: original error"}}}""")]
    public async Task Receive_ActualProtocolOrJavaScriptFailure_IsPreserved(string response)
    {
        using var channel = new NativeCdpResponseChannel();
        var pending = channel.Register(12);

        Assert.True(channel.TryReceive(Payload(channel, response)));
        Assert.Equal(response, await pending);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("""{"channel":1,"response":"bad"}""")]
    [InlineData("""{"channel":"wrong","response":"{}"}""")]
    public void Receive_UnrelatedOrMalformedMessage_DoesNotCompletePendingRequest(string payload)
    {
        using var channel = new NativeCdpResponseChannel();
        var pending = channel.Register(1);

        Assert.False(channel.TryReceive(payload));
        Assert.False(pending.IsCompleted);
        Assert.False(channel.TryReceive(Payload(channel, """{"id":"1","result":{}}""")));
        Assert.False(pending.IsCompleted);
    }

    static string Payload(NativeCdpResponseChannel channel, string response)
        => JsonSerializer.Serialize(new { channel = channel.BindingName, response });

    static string HandshakePayload(string script)
    {
        var start = script.IndexOf("binding(", StringComparison.Ordinal) + "binding(".Length;
        var end = script.IndexOf("); return 'sent'", start, StringComparison.Ordinal);
        return JsonSerializer.Deserialize<string>(script[start..end])!;
    }
}
