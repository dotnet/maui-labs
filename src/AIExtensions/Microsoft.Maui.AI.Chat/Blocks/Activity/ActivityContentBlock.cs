// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;

namespace Microsoft.Maui.AI.Chat;

/// <summary>A provider-defined activity represented by raw JSON content.</summary>
/// <remarks>
/// Raises <see cref="ContentBlock.PropertyChanged"/> when <see cref="ActivityType"/> changes and whenever
/// <see cref="Content"/> is set.
/// </remarks>
public class ActivityContentBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_contentChanged = new(nameof(Content));

    private string _activityType = string.Empty;
    private JsonElement _content;

    /// <summary>Gets or sets the provider-defined activity discriminator.</summary>
    public string ActivityType
    {
        get => _activityType;
        set => SetProperty(ref _activityType, value);
    }

    /// <summary>Gets or sets the provider-defined activity payload.</summary>
    public JsonElement Content
    {
        get => _content;
        set
        {
            // JsonElement has no value equality, so every payload counts as a change.
            _content = value;
            RaisePropertyChanged(s_contentChanged);
        }
    }
}
