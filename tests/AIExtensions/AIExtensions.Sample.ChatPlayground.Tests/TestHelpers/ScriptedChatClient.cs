using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// A chat client that answers requests in order from a script and records each request, so a test can drive the
/// chat engine without a model.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ScriptedResponse> _responses;

    public ScriptedChatClient(params ScriptedResponse[] responses) => _responses = new(responses);

    /// <summary>Gets every request, with a snapshot of its messages.</summary>
    public List<ScriptedRequest> Requests { get; } = [];

    /// <summary>Completes when a streamed response has yielded all of its updates.</summary>
    public TaskCompletionSource StreamCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a streamed response that waits for cancellation was canceled.</summary>
    public TaskCompletionSource StreamCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var script = Next(messages, options, streaming: false);
        if (script.Failure is { } failure)
            throw failure;
        return Task.FromResult(script.Response ?? script.Updates!.ToChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var script = Next(messages, options, streaming: true);
        foreach (var update in script.Updates ?? script.Response!.ToChatResponseUpdates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }

        StreamCompleted.TrySetResult();
        if (script.WaitForCancellation)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StreamCancelled.TrySetResult();
                throw;
            }
        }

        if (script.Failure is { } failure)
            throw failure;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private ScriptedResponse Next(IEnumerable<ChatMessage> messages, ChatOptions? options, bool streaming)
    {
        Requests.Add(new ScriptedRequest(messages.ToArray(), options, streaming));
        return _responses.TryDequeue(out var response)
            ? response
            : throw new InvalidOperationException($"No scripted response is left for request {Requests.Count}.");
    }
}

/// <summary>
/// One scripted answer: <see cref="Response"/> for a non-streaming request and <see cref="Updates"/> for a streaming
/// one, each derived from the other when only one is given.
/// </summary>
internal sealed record ScriptedResponse(ChatResponse? Response = null, IReadOnlyList<ChatResponseUpdate>? Updates = null)
{
    /// <summary>Gets an exception thrown after the updates (streaming) or instead of the response.</summary>
    public Exception? Failure { get; init; }

    /// <summary>Gets whether a stream waits, after its updates, until it is canceled.</summary>
    public bool WaitForCancellation { get; init; }

    public static ScriptedResponse Text(string text, string? modelId = null) =>
        new(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = modelId });

    public static ScriptedResponse Stream(params ChatResponseUpdate[] updates) => new(Updates: updates);
}

/// <summary>A request a <see cref="ScriptedChatClient"/> received.</summary>
internal sealed record ScriptedRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options, bool Streaming);
