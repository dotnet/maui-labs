// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A <see cref="UIAgent"/> that projects selected streamed response content into typed application
/// state while continuing to map all remaining content into visible blocks.
/// </summary>
/// <typeparam name="TState">The application state type.</typeparam>
public class UIAgent<TState> : UIAgent where TState : class, new()
{
    public UIAgent(IChatClient chatClient, TState? initialState = null)
        : base(chatClient)
    {
        State = new AgentState<TState>(initialState);
    }

    public UIAgent(IChatClient chatClient, ChatOptions chatOptions, TState? initialState = null)
        : base(chatClient, chatOptions)
    {
        State = new AgentState<TState>(initialState);
    }

    public UIAgent(
        IChatClient chatClient,
        ChatOptions chatOptions,
        ILoggerFactory? loggerFactory,
        TState? initialState = null)
        : base(chatClient, chatOptions, loggerFactory)
    {
        State = new AgentState<TState>(initialState);
    }

    public UIAgent(
        IChatClient chatClient,
        Action<UIAgentOptions>? configure,
        TState? initialState = null)
        : base(chatClient, configure)
    {
        State = new AgentState<TState>(initialState);
    }

    public UIAgent(
        IChatClient chatClient,
        Action<UIAgentOptions>? configure,
        ILoggerFactory? loggerFactory,
        TState? initialState = null)
        : base(chatClient, configure, loggerFactory)
    {
        State = new AgentState<TState>(initialState);
    }

    /// <summary>Gets the current typed application state.</summary>
    public AgentState<TState> State { get; }

    internal override object AgentStateObject => State;

    internal override ChatResponseUpdate ApplyStateMapper(ChatResponseUpdate update)
    {
        var mapped = base.ApplyStateMapper(update, out var stateContext);
        if (stateContext?.StateValue is TState typedState)
        {
            if (stateContext.IsPredictiveState)
                State.SetPredictiveValue(typedState);
            else
                State.Value = typedState;
        }
        else if (stateContext?.StateValue is not null)
        {
            throw new InvalidOperationException(
                $"The state mapper supplied '{stateContext.StateValue.GetType().FullName}', " +
                $"but this agent requires '{typeof(TState).FullName}'.");
        }
        return mapped;
    }

    internal override void RejectPendingPredictiveState() =>
        State.RejectPredictiveState();

    internal override object CaptureStateCheckpoint() =>
        State.CaptureCheckpoint();

    internal override void BeginStateRestore() =>
        State.ResetToInitialValue();

    internal override void CompleteStateRestore() =>
        State.RejectPredictiveState();

    internal override void RestoreStateCheckpoint(object? checkpoint)
    {
        if (checkpoint is AgentState<TState>.StateCheckpoint typedCheckpoint)
            State.RestoreCheckpoint(typedCheckpoint);
    }

    internal override void ResetState() =>
        State.ResetToInitialValue();
}
