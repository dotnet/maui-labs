// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI;

/// <summary>
/// Default implementation of <see cref="IAgentSession"/>.
/// Streams responses from an <see cref="IChatClient"/> and marshals UI updates to the main thread.
/// </summary>
public partial class AgentSession : ObservableObject, IAgentSession
{
    private readonly IChatClient _client;
    private readonly List<AITool> _tools = [];
    private readonly ConcurrentDictionary<string, InvocationContext> _invocations = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _pendingResponses = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isProcessing;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChatMessageViewModel> PendingMessages { get; } = [];

    public event Action<ReadOnlyMemory<byte>>? StateSnapshotReceived;
    public event Action<ReadOnlyMemory<byte>>? StateDeltaReceived;
    public event Action? ResponseUpdated;

    public AgentSession(IChatClient client)
    {
        // Wrap the client with function invocation support for tool execution
        _client = new ChatClientBuilder(client)
            .UseFunctionInvocation()
            .Build();
    }

    public void RegisterTool(AITool tool) => _tools.Add(tool);

    public void RegisterTools(params AITool[] tools) => _tools.AddRange(tools);

    public string? SystemInstructions { get; set; }

    public async Task SendAsync(params ChatMessage[] messages)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Add user messages to the completed list
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            foreach (var msg in messages)
            {
                Messages.Add(ChatMessageViewModel.FromChatMessage(msg));
            }

            IsProcessing = true;
        });

        try
        {
            // Build the full conversation history preserving all content types
            var chatHistory = new List<ChatMessage>();

            // Prepend system instructions if set
            if (!string.IsNullOrEmpty(SystemInstructions))
            {
                chatHistory.Add(new ChatMessage(ChatRole.System, SystemInstructions));
            }

            foreach (var vm in Messages)
            {
                chatHistory.Add(vm.ToChatMessage());
            }

            // Pass registered frontend tools via ChatOptions.Tools
            ChatOptions? options = null;
            if (_tools.Count > 0)
            {
                options = new ChatOptions
                {
                    Tools = [.. _tools]
                };
            }

            ChatMessageViewModel? currentPending = null;

            await foreach (var update in _client.GetStreamingResponseAsync(chatHistory, options, token))
            {
                if (token.IsCancellationRequested) break;

                // Ensure we have a pending message for streaming content
                if (currentPending is null)
                {
                    currentPending = new ChatMessageViewModel(update.Role ?? ChatRole.Assistant);
                    await MainThread.InvokeOnMainThreadAsync(() => PendingMessages.Add(currentPending));
                }
                else if (update.Role is not null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() => currentPending.Role = update.Role.Value);
                }

                await ProcessUpdateContentsAsync(update, currentPending, token);

                MainThread.BeginInvokeOnMainThread(() => ResponseUpdated?.Invoke());
            }

            // Promote pending message to completed
            if (currentPending is not null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    PendingMessages.Remove(currentPending);
                    if (!string.IsNullOrEmpty(currentPending.Text) || currentPending.Contents.Count > 0)
                    {
                        Messages.Add(currentPending);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Streaming was cancelled — clean up pending
            await MainThread.InvokeOnMainThreadAsync(() => PendingMessages.Clear());
        }
        catch (Exception ex)
        {
            // Log but don't crash the app
            Console.WriteLine($"AgentSession.SendAsync error: {ex}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PendingMessages.Clear();
                Messages.Add(new ChatMessageViewModel(ChatRole.Assistant, $"Error: {ex.Message}"));
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsProcessing = false);
        }
    }

    private async Task ProcessUpdateContentsAsync(
        ChatResponseUpdate update,
        ChatMessageViewModel currentPending,
        CancellationToken token)
    {
        foreach (var content in update.Contents)
        {
            if (token.IsCancellationRequested) break;

            try
            {
                switch (content)
                {
                    case TextContent textContent:
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            currentPending.Text += textContent.Text;
                            currentPending.Contents.Add(content);
                        });
                        break;

                    case FunctionCallContent functionCall:
                        var invocation = new InvocationContext(
                            functionCall.CallId,
                            functionCall.Name,
                            functionCall.Arguments);
                        _invocations[functionCall.CallId] = invocation;
                        await MainThread.InvokeOnMainThreadAsync(() => currentPending.Contents.Add(content));
                        break;

                    case FunctionResultContent functionResult:
                        if (_invocations.TryGetValue(functionResult.CallId, out var ctx))
                        {
                            ctx.SetResult(functionResult.Result);
                        }
                        await MainThread.InvokeOnMainThreadAsync(() => currentPending.Contents.Add(content));
                        break;

                    case DataContent dataContent:
                        HandleDataContent(dataContent);
                        await MainThread.InvokeOnMainThreadAsync(() => currentPending.Contents.Add(content));
                        break;

                    default:
                        await MainThread.InvokeOnMainThreadAsync(() => currentPending.Contents.Add(content));
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProcessUpdateContents error: {ex.Message}");
            }
        }
    }

    private void HandleDataContent(DataContent dataContent)
    {
        switch (dataContent.MediaType)
        {
            case "application/json":
                MainThread.BeginInvokeOnMainThread(() => StateSnapshotReceived?.Invoke(dataContent.Data));
                break;
            case "application/json-patch+json":
                MainThread.BeginInvokeOnMainThread(() => StateDeltaReceived?.Invoke(dataContent.Data));
                break;
        }
    }

    public Task<object> WaitForResponse(string key)
    {
        var tcs = _pendingResponses.GetOrAdd(key, _ => new TaskCompletionSource<object>());
        return tcs.Task;
    }

    public void ProvideResponse(string key, object response)
    {
        if (_pendingResponses.TryRemove(key, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }

    public void Reset()
    {
        _cts?.Cancel();
        _cts = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Clear();
            PendingMessages.Clear();
        });

        // Don't clear _tools — they are registered once by the ViewModel
        _invocations.Clear();

        // Cancel any outstanding HITL waits
        foreach (var kvp in _pendingResponses)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingResponses.Clear();
    }
}
