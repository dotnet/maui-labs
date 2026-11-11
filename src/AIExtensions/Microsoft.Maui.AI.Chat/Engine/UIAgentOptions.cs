// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Configuration for a <see cref="UIAgent"/>: the <see cref="ChatOptions"/> (instructions and tools),
/// optional conversation persistence, and custom block handlers.
/// </summary>
/// <remarks>Use <see cref="AddBlockHandler{TState}"/> to plug a custom <see cref="ContentBlockHandler{TState}"/>
/// into the pipeline.</remarks>
public class UIAgentOptions
{
    /// <summary>
    /// Gets or sets the chat options sent with every request, such as instructions, tools, tool mode,
    /// response format and model.
    /// </summary>
    /// <remarks>
    /// The agent reads the options for every request it sends and passes them to the chat client
    /// unchanged, unless it must add UI-action tools or a conversation id to a copy. An app can
    /// therefore change this instance between sends, for example to turn tools off or to set
    /// <see cref="ChatOptions.ToolMode"/>, <see cref="ChatOptions.ResponseFormat"/> or
    /// <see cref="ChatOptions.ModelId"/>, and the next send uses the new values with the same history.
    /// A change made while a send runs applies from its next request, such as the round that follows
    /// an approval or a UI action. To compute options for every request instead, add
    /// <c>ConfigureOptions</c> to the chat client pipeline, before <c>UseFunctionInvocation</c> when it
    /// changes tools.
    /// </remarks>
    public ChatOptions? ChatOptions { get; set; }

    /// <summary>
    /// Gets or sets a mapper that may consume assistant response content and supply typed application
    /// state for a <see cref="UIAgent{TState}"/>.
    /// </summary>
    /// <remarks>
    /// Content must be explicitly marked with <see cref="StateMapperContext.MarkHandled"/> to keep it
    /// out of the visible block pipeline. Supplying state alone does not consume content. The mapper
    /// and agent are single-thread-affine and not thread-safe.
    /// </remarks>
    public Action<StateMapperContext>? StateMapper { get; set; }

    /// <summary>
    /// Gets or sets the conversation thread that persists committed raw updates.
    /// </summary>
    /// <remarks>
    /// The thread and agent are single-thread-affine and not thread-safe. Implementations own
    /// persistence and serialization.
    /// </remarks>
    public IConversationThread? Thread { get; set; }

    /// <summary>
    /// Gets or sets services supplied to registered UI actions through
    /// <see cref="AIFunctionArguments.Services"/>.
    /// </summary>
    public IServiceProvider? Services { get; set; }

    internal List<IHandlerRegistration> HandlerRegistrations { get; } = new();
    internal Dictionary<string, UIActionRegistration> UIActions { get; } =
        new(StringComparer.Ordinal);

    public void AddBlockHandler<TState>(ContentBlockHandler<TState> handler)
        where TState : new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        HandlerRegistrations.Add(new HandlerRegistration<TState>(handler));
    }

    /// <summary>
    /// Registers a client-side action that is automatically executed by <see cref="AgentContext"/>
    /// without entering <see cref="ConversationStatus.AwaitingInput"/>.
    /// </summary>
    public void RegisterUIAction(AIFunction function)
        => RegisterUIAction(function, UIActionInvocationMode.Automatic);

    /// <summary>
    /// Registers a client-side action with the specified invocation mode.
    /// </summary>
    /// <param name="function">The function declared to the model and invoked by the client.</param>
    /// <param name="mode">Whether the action is invoked automatically or requires UI input.</param>
    public void RegisterUIAction(AIFunction function, UIActionInvocationMode mode)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!UIActions.TryAdd(function.Name, new UIActionRegistration(function, mode)))
        {
            throw new ArgumentException(
                $"A UI action named '{function.Name}' is already registered.",
                nameof(function));
        }
    }

    internal sealed record UIActionRegistration(AIFunction Function, UIActionInvocationMode Mode)
    {
        /// <summary>Gets whether the action is an <see cref="ApprovalRequiredAIFunction"/>.</summary>
        internal bool RequiresApproval { get; } =
            Function.GetService<ApprovalRequiredAIFunction>() is not null;
    }

    internal interface IHandlerRegistration
    {
        IHandlerEntry CreateEntry();
    }

    private sealed class HandlerRegistration<TState> : IHandlerRegistration where TState : new()
    {
        private readonly ContentBlockHandler<TState> _handler;

        internal HandlerRegistration(ContentBlockHandler<TState> handler)
        {
            _handler = handler;
        }

        public IHandlerEntry CreateEntry() => new HandlerEntry<TState>(_handler);
    }
}

/// <summary>Configures a <see cref="UIAgent{TState}"/>.</summary>
/// <typeparam name="TState">The type of the agent's state.</typeparam>
public sealed class UIAgentOptions<TState> : UIAgentOptions where TState : class, new()
{
    internal UIAgentOptions(TState? initialState)
    {
        State = new AgentState<TState>(initialState);
    }

    /// <summary>
    /// Gets the agent's observable state, the same instance as <see cref="UIAgent{TState}.State"/>, so a
    /// <see cref="UIAgentOptions.StateMapper"/> can read the current state and merge into it.
    /// </summary>
    public AgentState<TState> State { get; }
}
