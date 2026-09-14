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

    internal sealed record UIActionRegistration(AIFunction Function, UIActionInvocationMode Mode);

    internal interface IHandlerRegistration
    {
        IHandlerEntry CreateEntry();

        bool TryApplyFunctionResult(
            FunctionInvocationContentBlock block,
            FunctionResultContent result);
    }

    private sealed class HandlerRegistration<TState> : IHandlerRegistration where TState : new()
    {
        private readonly ContentBlockHandler<TState> _handler;

        internal HandlerRegistration(ContentBlockHandler<TState> handler)
        {
            _handler = handler;
        }

        public IHandlerEntry CreateEntry() => new HandlerEntry<TState>(_handler);

        public bool TryApplyFunctionResult(
            FunctionInvocationContentBlock block,
            FunctionResultContent result) =>
            block is TState state
            && _handler.TryApplyFunctionResult(state, result);
    }
}
