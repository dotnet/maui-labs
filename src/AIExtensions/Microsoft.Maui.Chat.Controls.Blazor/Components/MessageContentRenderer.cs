// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Registration model adapted from Microsoft.AspNetCore.Components.AI.BlockRenderer<TBlock>
// at commit 31b20463068f8d9ad900393bf96c9a182c397216. See Components/UPSTREAM-NOTES.md.

using Microsoft.AspNetCore.Components;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// Registers a Blazor render fragment for a specific <see cref="MessageContent"/> type
/// with the ambient <see cref="ChatViewContext"/>. The most recently registered renderer
/// that matches a content instance wins, so layer 2 (AI-specific renderers) can override
/// the defaults without changing this package.
/// </summary>
/// <typeparam name="TContent">The content type this renderer handles. Must derive from <see cref="MessageContent"/>.</typeparam>
/// <example>
/// <code>
/// &lt;ChatView Conversation="conversation"&gt;
///     &lt;MessageContentRenderer TContent="PollContent" Context="poll"&gt;
///         &lt;div class="mchat-poll"&gt;@poll.Question&lt;/div&gt;
///     &lt;/MessageContentRenderer&gt;
/// &lt;/ChatView&gt;
/// </code>
/// </example>
public class MessageContentRenderer<TContent> : IComponent, IDisposable
    where TContent : MessageContent
{
    private RenderHandle _renderHandle;
    private ChatContentRegistration? _registration;
    private bool _initialized;

    /// <summary>Gets or sets the ambient chat view context.</summary>
    [CascadingParameter]
    public ChatViewContext ViewContext { get; set; } = default!;

    /// <summary>Gets or sets the fragment rendered for a matching content instance.</summary>
    [Parameter]
    public RenderFragment<TContent>? ChildContent { get; set; }

    /// <summary>Gets or sets an optional predicate that narrows which content instances this renderer handles.</summary>
    [Parameter]
    public Func<TContent, bool>? When { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) =>
        _renderHandle = renderHandle;

    Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        if (ViewContext is null)
        {
            throw new InvalidOperationException(
                $"{nameof(MessageContentRenderer<TContent>)} must be placed inside a {nameof(ChatView)} or {nameof(ChatMessagesView)}.");
        }

        if (ChildContent is null)
        {
            throw new InvalidOperationException(
                $"{nameof(MessageContentRenderer<TContent>)} requires child content.");
        }

        if (!_initialized)
        {
            _initialized = true;

            _registration = new ChatContentRegistration
            {
                ContentType = typeof(TContent),
                // Capture 'this' so the lambdas read the latest When/ChildContent at invocation time.
                When = content => content is TContent typed && (When is null || When(typed)),
                Render = content => ChildContent((TContent)content),
            };

            ViewContext.AddRegistration(_registration);
        }

        // Renders no markup itself; the ChatViewContext registration change triggers the shell
        // to re-render the rows whose content bodies are now provided by this fragment.
        if (_renderHandle.IsInitialized)
        {
            _renderHandle.Render(_ => { });
        }

        return Task.CompletedTask;
    }

    /// <summary>Removes this renderer's registration from the view context.</summary>
    public void Dispose()
    {
        if (_registration is not null)
        {
            ViewContext?.RemoveRegistration(_registration);
            _registration = null;
        }

        GC.SuppressFinalize(this);
    }
}
