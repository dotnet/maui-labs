// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// One registered content renderer: what type it handles, an optional predicate, and the
/// factory that produces the rendered fragment for a matching content instance.
/// </summary>
/// <remarks>
/// The list of active registrations lives on <see cref="ChatViewContext"/> and is
/// cascaded to descendants. Consumers add registrations by dropping a
/// <see cref="MessageContentRenderer{TContent}"/> into a <see cref="ChatView"/>; the AI
/// bridge layer (shipped later) adds AI-specific content renderers exactly the same way.
/// </remarks>
public sealed class ChatContentRegistration
{
    /// <summary>The content type this registration handles.</summary>
    public required Type ContentType { get; init; }

    /// <summary>Predicate that decides whether this registration handles a given content instance.</summary>
    public required Func<MessageContent, bool> When { get; init; }

    /// <summary>Factory that produces the render fragment for a matching content instance.</summary>
    public required Func<MessageContent, RenderFragment> Render { get; init; }
}
