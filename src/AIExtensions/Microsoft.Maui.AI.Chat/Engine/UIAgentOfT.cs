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
        : this(chatClient, new UIAgentOptions<TState>(initialState), configure: null, loggerFactory: null)
    {
    }

    public UIAgent(IChatClient chatClient, ChatOptions chatOptions, TState? initialState = null)
        : this(chatClient, chatOptions, loggerFactory: null, initialState)
    {
    }

    public UIAgent(
        IChatClient chatClient,
        ChatOptions chatOptions,
        ILoggerFactory? loggerFactory,
        TState? initialState = null)
        : this(
            chatClient,
            new UIAgentOptions<TState>(initialState),
            options => options.ChatOptions = chatOptions,
            loggerFactory)
    {
    }

    /// <param name="chatClient">The chat client that produces model responses.</param>
    /// <param name="configure">
    /// Configures the agent. It runs after <see cref="UIAgentOptions{TState}.State"/> exists, so a state
    /// mapper can read the current state. A callback for the base <see cref="UIAgentOptions"/> works too.
    /// </param>
    /// <param name="initialState">The initial state value.</param>
    public UIAgent(
        IChatClient chatClient,
        Action<UIAgentOptions<TState>>? configure,
        TState? initialState = null)
        : this(chatClient, configure, loggerFactory: null, initialState)
    {
    }

    /// <param name="chatClient">The chat client that produces model responses.</param>
    /// <param name="configure">
    /// Configures the agent. It runs after <see cref="UIAgentOptions{TState}.State"/> exists, so a state
    /// mapper can read the current state. A callback for the base <see cref="UIAgentOptions"/> works too.
    /// </param>
    /// <param name="loggerFactory">The logger factory used to trace block mapping.</param>
    /// <param name="initialState">The initial state value.</param>
    public UIAgent(
        IChatClient chatClient,
        Action<UIAgentOptions<TState>>? configure,
        ILoggerFactory? loggerFactory,
        TState? initialState = null)
        : this(chatClient, new UIAgentOptions<TState>(initialState), configure, loggerFactory)
    {
    }

    private UIAgent(
        IChatClient chatClient,
        UIAgentOptions<TState> options,
        Action<UIAgentOptions<TState>>? configure,
        ILoggerFactory? loggerFactory)
        : base(chatClient, options, loggerFactory)
    {
        State = options.State;
        configure?.Invoke(options);
    }

    /// <summary>Gets the current typed application state.</summary>
    public AgentState<TState> State { get; }

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
