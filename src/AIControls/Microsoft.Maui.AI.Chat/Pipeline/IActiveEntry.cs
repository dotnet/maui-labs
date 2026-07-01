// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>A handler that has emitted a block and is actively receiving its streamed updates.</summary>
internal interface IActiveEntry
{
    ContentBlock Block { get; }
    HandleResult Invoke(BlockMappingContext context);
}
