// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.Maui.AI.Chat;

/// <summary>A provider-defined activity represented by raw JSON content.</summary>
public class ActivityContentBlock : ContentBlock
{
    /// <summary>Gets or sets the provider-defined activity discriminator.</summary>
    public string ActivityType { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider-defined activity payload.</summary>
    public JsonElement Content { get; set; }
}
