// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI;

/// <summary>
/// Represents the execution status of a plan step.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepStatus>))]
public enum StepStatus
{
    /// <summary>Step has not started.</summary>
    Pending = 0,

    /// <summary>Step has completed successfully.</summary>
    Completed = 1
}
