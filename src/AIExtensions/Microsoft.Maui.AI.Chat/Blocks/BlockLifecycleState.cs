// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Lifecycle of a <see cref="ContentBlock"/>: <c>Pending</c> (created), <c>Active</c> (currently
/// receiving streamed updates), or <c>Inactive</c> (finished).
/// </summary>
public enum BlockLifecycleState
{
    Pending,
    Active,
    Inactive,
}
