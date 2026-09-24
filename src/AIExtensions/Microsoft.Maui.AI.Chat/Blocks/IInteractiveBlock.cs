// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A block whose result is produced by the user or UI rather than the model.
/// <see cref="AgentContext"/> awaits <see cref="GetResultAsync"/> and feeds the result back into the
/// conversation, letting it resume.
/// </summary>
/// <remarks>Implemented by <see cref="ToolApprovalBlock"/>.</remarks>
public interface IInteractiveBlock
{
    Task<AIContent> GetResultAsync(CancellationToken cancellationToken = default);
}
