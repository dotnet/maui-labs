// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Maui.AI;

/// <summary>
/// Result provided by the user in response to a plan confirmation request (HITL).
/// </summary>
public class PlanConfirmationResult
{
    /// <summary>Whether the user confirmed the plan.</summary>
    public bool Confirmed { get; set; }

    /// <summary>Optional list of selected step indices the user approved.</summary>
    public List<int>? SelectedStepIndices { get; set; }
}

/// <summary>
/// Simple confirmation result for generic confirm/reject workflows.
/// </summary>
public class ConfirmChangesResult
{
    /// <summary>Whether the user confirmed the changes.</summary>
    public bool Confirmed { get; set; }
}
