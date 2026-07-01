// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A transient "the agent is working" placeholder (e.g. a spinner with "Thinking…") shown while
/// waiting for the model, before any content has streamed in.
/// </summary>
/// <remarks>
/// Not produced by the pipeline: <see cref="AgentContext"/> adds it at the start of a streaming round
/// and removes it (raising the block-removed callback) as soon as real content arrives or the turn
/// completes. It renders inline as an assistant message like any other block.
/// </remarks>
public sealed class ThinkingContentBlock : ContentBlock
{
    public ThinkingContentBlock(string text = "Thinking…")
    {
        Text = text;
    }

    public string Text { get; }
}
