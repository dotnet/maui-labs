// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI;

/// <summary>
/// Represents an agent execution plan with observable steps for Human-in-the-Loop workflows.
/// </summary>
public class Plan
{
    /// <summary>The ordered steps in this plan.</summary>
    [JsonPropertyName("steps")]
    public List<Step> Steps { get; set; } = [];

    /// <summary>Number of completed steps.</summary>
    [JsonIgnore]
    public int CompletedCount => Steps.Count(s => s.IsCompleted);

    /// <summary>Whether all steps have completed.</summary>
    [JsonIgnore]
    public bool IsComplete => Steps.Count > 0 && Steps.All(s => s.IsCompleted);
}
