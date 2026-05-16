// Copyright (c) Microsoft. All rights reserved.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI;

/// <summary>
/// Manages a streaming conversation session with an AI chat client.
/// Provides observable message collections for MAUI data binding.
/// </summary>
public interface IAgentSession : INotifyPropertyChanged
{
    /// <summary>All completed messages in the conversation.</summary>
    ObservableCollection<ChatMessageViewModel> Messages { get; }

    /// <summary>Messages currently being streamed (in-progress).</summary>
    ObservableCollection<ChatMessageViewModel> PendingMessages { get; }

    /// <summary>Whether the agent is currently processing a request.</summary>
    bool IsProcessing { get; }

    /// <summary>Registers a frontend tool that can be invoked by the agent.</summary>
    void RegisterTool(AITool tool);

    /// <summary>Registers multiple frontend tools.</summary>
    void RegisterTools(params AITool[] tools);

    /// <summary>
    /// Optional system instructions prepended to every agent call.
    /// Used to instruct the model to use specific tools.
    /// </summary>
    string? SystemInstructions { get; set; }

    /// <summary>
    /// Sends messages to the agent and begins streaming the response.
    /// </summary>
    Task SendAsync(params ChatMessage[] messages);

    /// <summary>Cancels the current streaming operation.</summary>
    void Cancel();

    /// <summary>Fires when an unhandled error occurs during streaming.</summary>
    event Action<Exception>? Failed;

    /// <summary>
    /// Human-in-the-loop: waits for a user response keyed by <paramref name="key"/>.
    /// </summary>
    Task<object> WaitForResponse(string key);

    /// <summary>
    /// Human-in-the-loop: provides the user response for the given <paramref name="key"/>.
    /// </summary>
    void ProvideResponse(string key, object response);

    /// <summary>Fires when a DataContent with media type "application/json" is received.</summary>
    event Action<ReadOnlyMemory<byte>>? StateSnapshotReceived;

    /// <summary>Fires when a DataContent with media type "application/json-patch+json" is received.</summary>
    event Action<ReadOnlyMemory<byte>>? StateDeltaReceived;

    /// <summary>Fires on each streaming update from the agent.</summary>
    event Action? ResponseUpdated;

    /// <summary>Clears all state for a new conversation.</summary>
    void Reset();
}
