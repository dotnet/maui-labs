// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A transient "the agent is working" placeholder (e.g. a spinner with "Thinking…") shown while
/// waiting for the model, before any content has streamed in.
/// </summary>
/// <remarks>
/// Not produced by the pipeline: <see cref="AgentContext"/> adds it at the start of a streaming round
/// and dismisses it (<see cref="Dismiss"/>) as soon as real content arrives or the turn completes.
/// It renders as an assistant bubble via the Controls layer, just like any other block.
/// </remarks>
public sealed class ThinkingContentBlock : ContentBlock
{
    public ThinkingContentBlock(string text = "Thinking…")
    {
        Text = text;
    }

    public string Text { get; }

    /// <summary>True once the block has been dismissed; the UI removes it when this becomes true.</summary>
    public bool IsDismissed { get; private set; }

    internal void Dismiss()
    {
        IsDismissed = true;
    }
}
