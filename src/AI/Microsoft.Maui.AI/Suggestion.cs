// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Maui.AI;

/// <summary>
/// A suggestion chip shown below the chat input.
/// </summary>
/// <param name="Text">Display text for the suggestion button.</param>
/// <param name="Message">Optional message to send when tapped (defaults to <paramref name="Text"/>).</param>
public record Suggestion(string Text, string? Message = null);
