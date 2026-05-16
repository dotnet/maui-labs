// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;

namespace Microsoft.Maui.AI.Controls.Tests;

public class ModelsTests
{
    [Fact]
    public void Plan_CompletedCount_ReturnsCorrectCount()
    {
        var plan = new Plan
        {
            Steps =
            [
                new Step { Description = "Step 1", Status = StepStatus.Completed },
                new Step { Description = "Step 2", Status = StepStatus.Pending },
                new Step { Description = "Step 3", Status = StepStatus.Completed },
            ]
        };

        Assert.Equal(2, plan.CompletedCount);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Plan_IsComplete_WhenAllStepsCompleted()
    {
        var plan = new Plan
        {
            Steps =
            [
                new Step { Description = "Step 1", Status = StepStatus.Completed },
                new Step { Description = "Step 2", Status = StepStatus.Completed },
            ]
        };

        Assert.True(plan.IsComplete);
    }

    [Fact]
    public void Plan_IsComplete_FalseWhenEmpty()
    {
        var plan = new Plan();
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void Step_IsCompleted_ReflectsStatus()
    {
        var step = new Step { Description = "Test", Status = StepStatus.Pending };
        Assert.False(step.IsCompleted);

        step.Status = StepStatus.Completed;
        Assert.True(step.IsCompleted);
    }

    [Fact]
    public void Step_StatusText_ShowsCorrectEmoji()
    {
        var pending = new Step { Status = StepStatus.Pending };
        var completed = new Step { Status = StepStatus.Completed };

        Assert.Equal("⏳", pending.StatusText);
        Assert.Equal("✅", completed.StatusText);
    }

    [Fact]
    public void Step_PropertyChanged_FiresOnStatusChange()
    {
        var step = new Step { Status = StepStatus.Pending };
        var changedProps = new List<string>();
        step.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        step.Status = StepStatus.Completed;

        Assert.Contains("Status", changedProps);
        Assert.Contains("IsCompleted", changedProps);
        Assert.Contains("StatusText", changedProps);
    }

    [Fact]
    public void PlanConfirmationResult_Serialization()
    {
        var result = new PlanConfirmationResult
        {
            Confirmed = true,
            SelectedStepIndices = [0, 2, 4]
        };

        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<PlanConfirmationResult>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Confirmed);
        Assert.Equal([0, 2, 4], deserialized.SelectedStepIndices);
    }

    [Fact]
    public void ConfirmChangesResult_Serialization()
    {
        var result = new ConfirmChangesResult { Confirmed = false };

        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<ConfirmChangesResult>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Confirmed);
    }

    [Fact]
    public void JsonPatchOperation_Serialization()
    {
        var op = new JsonPatchOperation
        {
            Op = "replace",
            Path = "/steps/0/status",
            Value = JsonSerializer.Deserialize<JsonElement>("\"completed\"")
        };

        var json = JsonSerializer.Serialize(op);
        var deserialized = JsonSerializer.Deserialize<JsonPatchOperation>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("replace", deserialized.Op);
        Assert.Equal("/steps/0/status", deserialized.Path);
        Assert.Equal("completed", deserialized.Value?.GetString());
    }

    [Fact]
    public void StepStatus_JsonSerialization()
    {
        var step = new Step { Description = "Test", Status = StepStatus.Completed };
        var json = JsonSerializer.Serialize(step);

        Assert.Contains("\"completed\"", json.ToLowerInvariant());
    }
}
