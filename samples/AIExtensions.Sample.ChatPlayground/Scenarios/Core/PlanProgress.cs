using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>A plan: the agent's state in the plan scenario, and the result of <c>create_plan</c>.</summary>
public sealed class PlanState
{
    [JsonPropertyName("steps")]
    public List<PlanStep> Steps { get; set; } = [];

    /// <summary>Gets how many steps are completed.</summary>
    [JsonIgnore]
    public int CompletedCount => Steps.Count(static step => step.IsCompleted);

    /// <summary>Gets whether every step is completed.</summary>
    [JsonIgnore]
    public bool IsDone => Steps.Count > 0 && Steps.All(static step => step.IsCompleted);

    /// <summary>Creates a plan whose steps are all pending.</summary>
    public static PlanState FromDescriptions(IEnumerable<string> descriptions) => new()
    {
        Steps = [.. descriptions.Where(static text => !string.IsNullOrWhiteSpace(text))
            .Select(static text => new PlanStep { Description = text.Trim() })],
    };

    /// <summary>Copies the plan, so a change creates a new state rather than editing the current one.</summary>
    public PlanState Clone() => new() { Steps = [.. Steps.Select(static step => step.Clone())] };
}

/// <summary>One step of a <see cref="PlanState"/>.</summary>
public sealed class PlanStep
{
    /// <summary>The status of a step that is not done yet.</summary>
    public const string Pending = "pending";

    /// <summary>The status of a step that is done.</summary>
    public const string Completed = "completed";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets <see cref="Pending"/> or <see cref="Completed"/>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = Pending;

    /// <summary>Gets or sets whether the agent is working on the step: its update was called but has not returned.</summary>
    [JsonIgnore]
    public bool IsRunning { get; set; }

    /// <summary>Gets whether the step is done.</summary>
    [JsonIgnore]
    public bool IsCompleted => string.Equals(Status, Completed, StringComparison.OrdinalIgnoreCase);

    public PlanStep Clone() => new() { Description = Description, Status = Status, IsRunning = IsRunning };
}

/// <summary>The result of <c>update_plan_step</c>: the step's new status and description.</summary>
public sealed class PlanStepUpdate
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// The tools an agent plans with: <c>create_plan</c> sets out the steps, and <c>update_plan_step</c> reports progress on
/// one. They stand in for work the agent does, so updating a step takes a moment.
/// </summary>
public sealed class PlanTools
{
    /// <summary>The name of the tool that creates a plan.</summary>
    public const string CreatePlanName = "create_plan";

    /// <summary>The name of the tool that updates a step.</summary>
    public const string UpdatePlanStepName = "update_plan_step";

    /// <param name="stepLatency">How long updating a step takes.</param>
    public PlanTools(TimeSpan stepLatency)
    {
        StepLatency = stepLatency;
        CreatePlan = AIFunctionFactory.Create(
            CreatePlanCore,
            new AIFunctionFactoryOptions
            {
                Name = CreatePlanName,
                Description = "Creates a plan with several steps. All steps start pending.",
                SerializerOptions = ScenarioJson.Options,
            });
        UpdatePlanStep = AIFunctionFactory.Create(
            UpdatePlanStepAsync,
            new AIFunctionFactoryOptions
            {
                Name = UpdatePlanStepName,
                Description = "Updates one step of the current plan with a new status or description.",
                SerializerOptions = ScenarioJson.Options,
            });
    }

    /// <summary>Gets how long updating a step takes.</summary>
    public TimeSpan StepLatency { get; }

    /// <summary>Gets <c>create_plan(steps)</c>.</summary>
    public AIFunction CreatePlan { get; }

    /// <summary>Gets <c>update_plan_step(index, status, description)</c>.</summary>
    public AIFunction UpdatePlanStep { get; }

    private static PlanState CreatePlanCore(
        [Description("The step descriptions, in order.")] List<string> steps) =>
        PlanState.FromDescriptions(steps);

    private async Task<PlanStepUpdate> UpdatePlanStepAsync(
        [Description("The zero-based index of the step.")] int index,
        [Description("The new status: pending or completed.")] string? status = null,
        [Description("A new description for the step, if it changed.")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (StepLatency > TimeSpan.Zero)
            await Task.Delay(StepLatency, cancellationToken);

        return new PlanStepUpdate
        {
            Index = index,
            Status = string.Equals(status, PlanStep.Pending, StringComparison.OrdinalIgnoreCase)
                ? PlanStep.Pending
                : PlanStep.Completed,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
    }
}

/// <summary>
/// The plan as one in-chat activity: its steps with what is done, what the agent works on, and what is next. It updates
/// in place while the agent works through the plan.
/// </summary>
public sealed class PlanActivityBlock : ActivityContentBlock
{
    private readonly HashSet<string> _pendingCalls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stepCalls = new(StringComparer.Ordinal);

    /// <summary>Initializes an empty plan activity.</summary>
    public PlanActivityBlock() => ActivityType = "plan";

    /// <summary>Gets the plan's steps.</summary>
    public List<PlanStep> Steps { get; } = [];

    /// <summary>Gets how many steps are completed.</summary>
    public int CompletedCount => Steps.Count(static step => step.IsCompleted);

    /// <summary>Gets whether every step is completed and no call is still running.</summary>
    public bool IsDone => Steps.Count > 0 && Steps.All(static step => step.IsCompleted) && _pendingCalls.Count == 0;

    internal bool IsPlanCall(string callId) => _pendingCalls.Contains(callId) || _stepCalls.ContainsKey(callId);

    internal void ReplaceSteps(IEnumerable<PlanStep> steps)
    {
        Steps.Clear();
        Steps.AddRange(steps.Select(static step => step.Clone()));
    }

    internal void StartCall(string callId, int? stepIndex)
    {
        _pendingCalls.Add(callId);
        if (stepIndex is { } index)
            _stepCalls[callId] = index;
    }

    internal bool TryCompleteCall(string callId, out int? stepIndex)
    {
        stepIndex = _stepCalls.TryGetValue(callId, out var index) ? index : null;
        return _pendingCalls.Remove(callId);
    }
}

/// <summary>
/// Maps a plan's tool calls into one <see cref="PlanActivityBlock"/> that updates in place: <c>create_plan</c> starts
/// it, each <c>update_plan_step</c> call marks its step as running, and each result marks it completed. It claims the
/// calls and their results, so the chat shows the plan rather than a tool card per call.
/// </summary>
/// <param name="currentPlan">
/// Gets the plan an earlier response created, so updates to it in a later response still show as the plan.
/// </param>
public sealed class PlanActivityHandler(Func<PlanState?>? currentPlan = null) : ActivityHandler<PlanActivityBlock>
{
    /// <inheritdoc />
    protected override bool TryCreateBlock(BlockMappingContext context, PlanActivityBlock block)
    {
        FunctionCallContent? first = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is FunctionCallContent { Name: PlanTools.CreatePlanName or PlanTools.UpdatePlanStepName } call)
            {
                first = call;
                break;
            }
        }

        if (first is null)
            return false;

        if (first.Name == PlanTools.UpdatePlanStepName)
        {
            // An update without a plan in this response continues the plan the agent made earlier.
            if (currentPlan?.Invoke() is not { Steps.Count: > 0 } plan)
                return false;
            block.ReplaceSteps(plan.Steps.Select(static step => new PlanStep
            {
                Description = step.Description,
                Status = step.Status,
            }));
        }

        Apply(context, block);
        return true;
    }

    /// <inheritdoc />
    protected override bool TryUpdateBlock(BlockMappingContext context, PlanActivityBlock block, out bool isCompleted)
    {
        var changed = Apply(context, block);
        isCompleted = block.IsDone;
        return changed;
    }

    /// <inheritdoc />
    protected override void OnContentUpdated(PlanActivityBlock block)
    {
        // The generic activity payload, for anything that reads activities without knowing this block type.
        block.Content = JsonSerializer.SerializeToElement(
            new PlanState { Steps = block.Steps },
            ScenarioJsonContext.Default.PlanState);
    }

    private static bool Apply(BlockMappingContext context, PlanActivityBlock block)
    {
        var changed = false;
        foreach (var content in context.UnhandledContents)
        {
            switch (content)
            {
                case FunctionCallContent { Name: PlanTools.CreatePlanName } call:
                    context.MarkHandled(call);
                    changed = true;
                    if (block.IsPlanCall(call.CallId))
                        break;
                    var descriptions = call.Arguments is { } arguments && arguments.TryGetValue("steps", out var steps)
                        ? ScenarioJson.AsStrings(steps)
                        : [];
                    block.ReplaceSteps(PlanState.FromDescriptions(descriptions).Steps);
                    block.StartCall(call.CallId, stepIndex: null);
                    break;

                case FunctionCallContent { Name: PlanTools.UpdatePlanStepName } call:
                    context.MarkHandled(call);
                    changed = true;
                    if (block.IsPlanCall(call.CallId))
                        break;
                    var index = ScenarioJson.ReadInt(call.Arguments, "index");
                    if (index is { } stepIndex && stepIndex >= 0 && stepIndex < block.Steps.Count)
                    {
                        block.Steps[stepIndex].IsRunning = true;
                        if (ScenarioJson.ReadString(call.Arguments, "description") is { Length: > 0 } description)
                            block.Steps[stepIndex].Description = description;
                    }
                    block.StartCall(call.CallId, index);
                    break;

                case FunctionResultContent result when block.TryCompleteCall(result.CallId, out var resultIndex):
                    context.MarkHandled(result);
                    changed = true;
                    if (resultIndex is { } updated && updated >= 0 && updated < block.Steps.Count)
                    {
                        var step = block.Steps[updated];
                        step.IsRunning = false;
                        if (ScenarioJson.Read<PlanStepUpdate>(result.Result) is { } update)
                        {
                            step.Status = update.Status ?? PlanStep.Completed;
                            if (!string.IsNullOrWhiteSpace(update.Description))
                                step.Description = update.Description;
                        }
                    }
                    else if (ScenarioJson.Read<PlanState>(result.Result) is { Steps.Count: > 0 } created)
                    {
                        block.ReplaceSteps(created.Steps);
                    }
                    break;
            }
        }

        return changed;
    }
}

/// <summary>
/// Keeps a <see cref="PlanState"/> in step with the plan's tool calls, as the agent's typed state: the panel beside the
/// chat shows it. It reads the calls and results but leaves them for the activity block.
/// </summary>
public sealed class PlanStateMapper
{
    private readonly Dictionary<string, int?> _stepCalls = new(StringComparer.Ordinal);

    /// <summary>Maps one update of the response.</summary>
    /// <param name="context">The update.</param>
    /// <param name="current">The current plan.</param>
    public void Map(StateMapperContext context, PlanState current)
    {
        PlanState? next = null;
        foreach (var content in context.Update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent { Name: PlanTools.CreatePlanName } call:
                    var steps = call.Arguments is { } arguments && arguments.TryGetValue("steps", out var value)
                        ? ScenarioJson.AsStrings(value)
                        : [];
                    next = PlanState.FromDescriptions(steps);
                    _stepCalls[call.CallId] = null;
                    break;

                case FunctionCallContent { Name: PlanTools.UpdatePlanStepName } call:
                    var index = ScenarioJson.ReadInt(call.Arguments, "index");
                    _stepCalls[call.CallId] = index;
                    next = Update(next ?? current, index, step =>
                    {
                        step.IsRunning = true;
                        if (ScenarioJson.ReadString(call.Arguments, "description") is { Length: > 0 } description)
                            step.Description = description;
                    }) ?? next;
                    break;

                case FunctionResultContent result when _stepCalls.Remove(result.CallId, out var stepIndex):
                    if (stepIndex is null)
                        break;
                    var update = ScenarioJson.Read<PlanStepUpdate>(result.Result);
                    next = Update(next ?? current, stepIndex, step =>
                    {
                        step.IsRunning = false;
                        step.Status = update?.Status ?? PlanStep.Completed;
                        if (!string.IsNullOrWhiteSpace(update?.Description))
                            step.Description = update.Description;
                    }) ?? next;
                    break;
            }
        }

        if (next is not null)
            context.SetState(next);
    }

    private static PlanState? Update(PlanState plan, int? index, Action<PlanStep> change)
    {
        if (index is not { } stepIndex || stepIndex < 0 || stepIndex >= plan.Steps.Count)
            return null;

        var updated = plan.Clone();
        change(updated.Steps[stepIndex]);
        return updated;
    }
}
