// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>
/// The result <see cref="FunctionInvokingChatClient"/> sends the model for a rejected call. The wording belongs
/// to Microsoft.Extensions.AI and changes between its releases, so tests expect it only through here.
/// </summary>
internal static class RejectedCall
{
    internal static string Result(string reason)
        => $"Tool call invocation was rejected by the approver and must not be retried. {reason}";
}
