// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// State of an <see cref="AgentContext"/>: <c>Idle</c>, <c>Streaming</c>, <c>AwaitingInput</c> (paused for
/// an interactive block such as an approval), or <c>Error</c>.
/// </summary>
public enum ConversationStatus
{
    Idle,
    Streaming,
    AwaitingInput,
    Error
}
