// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// An error surfaced to the user as a message. Not real model content — it lets a failure
/// <em>render</em> as a bubble in the conversation.
/// </summary>
/// <remarks>
/// Added to the current turn by <see cref="AgentContext"/> when streaming throws (alongside setting
/// <see cref="ConversationStatus.Error"/>). Rendered by the Controls layer like any other block.
/// </remarks>
public sealed class ErrorContentBlock : ContentBlock
{
    public ErrorContentBlock(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
