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
    /// Return <see langword="true"/> when the update contained state handled by the mapper. Content
    /// must also be marked with <see cref="StateMapperContext.MarkHandled"/> to keep it out of the
    /// visible block pipeline. The mapper and agent are single-thread-affine and not thread-safe.
    /// </remarks>
    public Func<StateMapperContext, bool>? StateMapper { get; set; }

    /// <summary>
    /// Gets or sets the conversation thread that persists committed raw updates.
    /// </summary>
    /// <remarks>
    /// The thread and agent are single-thread-affine and not thread-safe. Implementations own
    /// persistence and serialization.
    /// </remarks>
    public IConversationThread? Thread { get; set; }

    internal List<IHandlerRegistration> HandlerRegistrations { get; } = new();

    public void AddBlockHandler<TState>(ContentBlockHandler<TState> handler)
        where TState : new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        HandlerRegistrations.Add(new HandlerRegistration<TState>(handler));
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
