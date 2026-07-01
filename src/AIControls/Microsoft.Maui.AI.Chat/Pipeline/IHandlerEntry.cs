// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>A registered handler in the <see cref="BlockMappingPipeline"/> that can try to claim content and become active.</summary>
internal interface IHandlerEntry
{
    IActiveEntry? TryHandle(BlockMappingContext context);
}
