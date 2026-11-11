using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>A streaming <see cref="IChatClient"/> that plays one script per request.</summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Script> _scripts = new();

    public delegate IAsyncEnumerable<ChatResponseUpdate> Script(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken);

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public List<ChatOptions?> RequestOptions { get; } = [];

    public bool IsDisposed { get; private set; }

    /// <summary>Gets the user text of the last request.</summary>
    public string? LastUserText => Requests.LastOrDefault()?.LastOrDefault(static m => m.Role == ChatRole.User)?.Text;

    public ScriptedChatClient Reply(params string[] tokens)
    {
        _scripts.Enqueue((_, _, cancellationToken) => Stream(tokens.Select(static t => new ChatResponseUpdate(ChatRole.Assistant, t)), cancellationToken));
        return this;
    }

    public ScriptedChatClient Reply(params AIContent[] contents)
    {
        _scripts.Enqueue((_, _, cancellationToken) => Stream(contents.Select(static c => new ChatResponseUpdate(ChatRole.Assistant, [c])), cancellationToken));
        return this;
    }

    /// <summary>Streams <paramref name="before"/>, waits for <paramref name="gate"/>, then streams <paramref name="after"/>.</summary>
    public ScriptedChatClient ReplyAfter(Task gate, string before, string after)
    {
        _scripts.Enqueue((_, _, cancellationToken) => Gated(gate, before, after, cancellationToken));
        return this;
    }

    /// <summary>Waits for <paramref name="gate"/> before streaming anything.</summary>
    public ScriptedChatClient WaitThenReply(Task gate, string text)
    {
        _scripts.Enqueue((_, _, cancellationToken) => Gated(gate, before: null, text, cancellationToken));
        return this;
    }

    /// <summary>Streams whatever the test writes until it completes the writer.</summary>
    public ChannelWriter<ChatResponseUpdate> ReplyLive()
    {
        var channel = Channel.CreateUnbounded<ChatResponseUpdate>();
        _scripts.Enqueue((_, _, cancellationToken) => channel.Reader.ReadAllAsync(cancellationToken));
        return channel.Writer;
    }

    /// <summary>Asks the user to approve a call to <paramref name="toolName"/>.</summary>
    public ScriptedChatClient RequestApproval(string toolName, IDictionary<string, object?>? arguments = null)
    {
        var call = new FunctionCallContent("call-" + toolName, toolName, arguments);
        return Reply(new ToolApprovalRequestContent("approval-" + toolName, call));
    }

    public ScriptedChatClient Fail(Exception exception)
    {
        _scripts.Enqueue((_, _, cancellationToken) => Throw(exception, cancellationToken));
        return this;
    }

    public ScriptedChatClient Then(Script script)
    {
        _scripts.Enqueue(script);
        return this;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        Requests.Add(snapshot);
        RequestOptions.Add(options);
        var script = _scripts.Count > 0
            ? _scripts.Dequeue()
            : (_, _, token) => Stream([new ChatResponseUpdate(ChatRole.Assistant, "OK")], token);

        await foreach (var update in script(snapshot, options, cancellationToken).WithCancellation(cancellationToken))
            yield return update;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
            updates.Add(update);
        return updates.ToChatResponse();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => IsDisposed = true;

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream(
        IEnumerable<ChatResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var update in updates)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Gated(
        Task gate,
        string? before,
        string after,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (before is not null)
            yield return new ChatResponseUpdate(ChatRole.Assistant, before);

        await gate.WaitAsync(cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, after);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Throw(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (exception is not null)
            throw exception;
        yield break;
    }
}

internal static class ChatStreamExtensions
{
    public static void Text(this ChannelWriter<ChatResponseUpdate> writer, string text) =>
        writer.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, text));

    public static void Content(this ChannelWriter<ChatResponseUpdate> writer, AIContent content) =>
        writer.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, [content]));
}
