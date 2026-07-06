// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>State of a <see cref="ToolApprovalBlock"/>: <c>Pending</c>, <c>Approved</c>, or <c>Rejected</c>.</summary>
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected
}
