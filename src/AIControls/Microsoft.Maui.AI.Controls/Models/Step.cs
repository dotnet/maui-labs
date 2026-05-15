// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Microsoft.Maui.AI;

/// <summary>
/// A single step in an agent plan, with observable status for real-time UI updates.
/// </summary>
public partial class Step : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [property: JsonPropertyName("status")]
    private StepStatus _status;

    [ObservableProperty]
    [property: JsonPropertyName("description")]
    private string _description = string.Empty;

    /// <summary>UI-only: whether the user has selected this step (for confirmation workflows).</summary>
    [ObservableProperty]
    [JsonIgnore]
    private bool _isSelected;

    /// <summary>True when the step has completed.</summary>
    [JsonIgnore]
    public bool IsCompleted => Status == StepStatus.Completed;

    /// <summary>Display text for the status.</summary>
    [JsonIgnore]
    public string StatusText => Status switch
    {
        StepStatus.Completed => "✅",
        _ => "⏳"
    };
}
