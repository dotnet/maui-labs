using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Schema-shaped validation findings for a <see cref="MauiTestPlan"/>.</summary>
public sealed class MauiTestPlanValidation
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates the stable, executable-independent portion of the <c>maui-test-plan-v1</c>
/// contract. Hosts add workspace and current-flow checks before persisting a sidecar.
/// </summary>
public static class MauiTestPlanValidator
{
    public static MauiTestPlanValidation Validate(MauiTestPlan? plan)
    {
        var result = new MauiTestPlanValidation();
        if (plan is null)
        {
            result.Errors.Add("Plan is required.");
            return result;
        }

        if (plan.Schema != 1)
            result.Errors.Add("schema must be 1 for test-plan-v1.");
        Require(plan.PlanId, "planId", result);
        if (plan.Revision is null or < 1)
            result.Errors.Add("revision must be an integer greater than or equal to 1.");
        if (plan.Title is { Length: > 512 })
            result.Errors.Add("title is too long.");
        Require(plan.Goal, "goal", result);

        if (plan.Flow is null)
        {
            result.Errors.Add("flow is required.");
        }
        else
        {
            Require(plan.Flow.Digest, "flow.digest", result);
            if (plan.Flow.Path is { Length: > 1024 })
                result.Errors.Add("flow.path is too long.");
        }

        if (!MauiFlowSideEffectPolicies.IsKnown(plan.SideEffectPolicy))
            result.Errors.Add("sideEffectPolicy must be none, app-state-resettable, test-tenant-resettable, compensated, or non-replayable.");

        if (plan.Reset is null)
            result.Errors.Add("reset is required.");
        if (plan.Provenance is null)
        {
            result.Errors.Add("provenance is required.");
        }
        else
        {
            Require(plan.Provenance.ActorKind, "provenance.actorKind", result);
            Require(plan.Provenance.Channel, "provenance.channel", result);
        }

        var scenarios = plan.Scenarios ?? [];
        var preconditions = plan.Preconditions ?? [];
        var acceptanceCriteria = plan.AcceptanceCriteria ?? [];
        if (plan.Scenarios is null) result.Errors.Add("scenarios must be an array.");
        if (plan.Preconditions is null) result.Errors.Add("preconditions must be an array.");
        if (plan.AcceptanceCriteria is null) result.Errors.Add("acceptanceCriteria must be an array.");

        ValidateIds(
            scenarios.Where(static scenario => scenario is not null).Select(static scenario => scenario.ScenarioId),
            "scenarioId",
            result);
        ValidateIds(
            preconditions.Where(static precondition => precondition is not null).Select(static precondition => precondition.PreconditionId),
            "preconditionId",
            result);
        ValidateIds(
            acceptanceCriteria.Where(static criterion => criterion is not null).Select(static criterion => criterion.CriterionId),
            "criterionId",
            result);
        ValidateStringList(plan.RequiredPlatforms ?? [], "requiredPlatforms", result);
        ValidateRepairPolicy(plan.RepairPolicy, result);
        ValidateBusinessOracles(plan, acceptanceCriteria, result);

        var criteria = new HashSet<string>(
            acceptanceCriteria
                .Where(static criterion => criterion is not null)
                .Select(static criterion => criterion.CriterionId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>(),
            StringComparer.Ordinal);
        foreach (var scenario in scenarios.Where(static scenario => scenario is not null))
        {
            foreach (var criterionId in (scenario.AcceptanceCriterionIds ?? []).Where(static id => !string.IsNullOrWhiteSpace(id)))
            {
                if (!criteria.Contains(criterionId))
                    result.Errors.Add($"scenario '{scenario.ScenarioId ?? "(unnamed)"}' references unknown acceptance criterion '{criterionId}'.");
            }
        }

        foreach (var requirement in (plan.Requirements?.RequiredCapabilities ?? []).Where(static requirement => requirement is not null))
        {
            if (string.IsNullOrWhiteSpace(requirement.Name))
                result.Errors.Add("requirements.requiredCapabilities entries require a name.");
        }
        foreach (var requirement in (plan.Requirements?.RequiredSemantics ?? []).Where(static requirement => requirement is not null))
        {
            if (string.IsNullOrWhiteSpace(requirement.Name))
                result.Errors.Add("requirements.requiredSemantics entries require a name.");
        }

        if (plan.ParsedSideEffectPolicy == MauiFlowSideEffectPolicy.Compensated &&
            plan.Compensator is null)
        {
            result.Warnings.Add("compensated plans should declare a compensator before replay is admitted.");
        }
        if (plan.ParsedSideEffectPolicy == MauiFlowSideEffectPolicy.NonReplayable)
        {
            result.Warnings.Add("non-replayable plans can be saved, but never authorize automatic replay or repair.");
        }
        return result;
    }

    /// <summary>
    /// Parses and validates a plan while retaining additive JSON fields through
    /// <see cref="JsonExtensionDataAttribute"/> on the contracts.
    /// </summary>
    public static MauiTestPlanValidation ValidateJson(string? json, out MauiTestPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            var missing = new MauiTestPlanValidation();
            missing.Errors.Add("Plan JSON is required.");
            return missing;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                var notObject = new MauiTestPlanValidation();
                notObject.Errors.Add("Plan JSON must be an object.");
                return notObject;
            }
            foreach (var required in RequiredProperties)
            {
                if (!document.RootElement.TryGetProperty(required, out _))
                {
                    var missingProperty = new MauiTestPlanValidation();
                    missingProperty.Errors.Add($"{required} is required.");
                    return missingProperty;
                }
            }
            plan = JsonSerializer.Deserialize(json, MauiTestingJsonContext.Default.MauiTestPlan);
        }

        catch (JsonException ex)
        {
            var invalid = new MauiTestPlanValidation();
            invalid.Errors.Add($"Invalid plan JSON: {ex.Message}");
            return invalid;
        }

        return Validate(plan);
    }

    private static readonly string[] RequiredProperties =
    [
        "schema",
        "planId",
        "revision",
        "flow",
        "goal",
        "scenarios",
        "preconditions",
        "reset",
        "acceptanceCriteria",
        "sideEffectPolicy",
        "provenance",
    ];

    private static void Require(string? value, string name, MauiTestPlanValidation result)
    {
        if (string.IsNullOrWhiteSpace(value))
            result.Errors.Add($"{name} is required.");
        else if (value.Length > 16_384)
            result.Errors.Add($"{name} is too long.");
    }

    private static void ValidateIds(
        IEnumerable<string?> ids,
        string kind,
        MauiTestPlanValidation result)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in ids)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.Errors.Add($"{kind} is required.");
                continue;
            }
            if (!seen.Add(value))
                result.Errors.Add($"{kind} '{value}' is duplicated.");
        }
    }

    private static void ValidateStringList(
        IEnumerable<string> values,
        string name,
        MauiTestPlanValidation result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.Errors.Add($"{name} cannot contain an empty value.");
                continue;
            }
            if (!seen.Add(value))
                result.Errors.Add($"{name} contains duplicate value '{value}'.");
        }
    }

    private static void ValidateRepairPolicy(MauiFlowRepairPolicy? policy, MauiTestPlanValidation result)
    {
        if (policy is null)
            return;

        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "automation-id",
            "exact-text",
        };
        ValidateStringList(policy.AllowedCandidateKinds, "repairPolicy.allowedCandidateKinds", result);
        foreach (var kind in policy.AllowedCandidateKinds)
        {
            if (!supported.Contains(kind))
            {
                result.Errors.Add(
                    $"repairPolicy.allowedCandidateKinds contains unsupported executable kind '{kind}'.");
            }
        }
        ValidateStringList(policy.AllowedRiskFlags, "repairPolicy.allowedRiskFlags", result);
        foreach (var risk in policy.AllowedRiskFlags)
        {
            if (!string.Equals(risk, "localization", StringComparison.Ordinal))
            {
                result.Errors.Add(
                    $"repairPolicy.allowedRiskFlags cannot allow unsafe risk '{risk}'.");
            }
        }
        if (policy.MaxCandidates is < 1 or > 8)
            result.Errors.Add("repairPolicy.maxCandidates must be between 1 and 8.");
        if (policy.MinimumScore is < 0 or > 1)
            result.Errors.Add("repairPolicy.minimumScore must be between 0 and 1.");
        if (policy.MinimumScoreGap is < 0 or > 1)
            result.Errors.Add("repairPolicy.minimumScoreGap must be between 0 and 1.");
    }

    private static void ValidateBusinessOracles(
        MauiTestPlan plan,
        IEnumerable<MauiAcceptanceCriterion> acceptanceCriteria,
        MauiTestPlanValidation result)
    {
        if ((plan.IndependentBusinessOracles ?? []).Any(static oracle => oracle is null))
            result.Errors.Add("independentBusinessOracles cannot contain null entries.");
        if ((plan.BusinessOracles ?? []).Any(static oracle => oracle is null))
            result.Errors.Add("businessOracles cannot contain null entries.");
        var declarations = (plan.IndependentBusinessOracles ?? [])
            .Where(static oracle => oracle is not null)
            .Select(static oracle => (
                oracle.OracleId,
                oracle.Required,
                oracle.Independent,
                Source: "independentBusinessOracles"))
            .Concat((plan.BusinessOracles ?? [])
                .Where(static oracle => oracle is not null)
                .Select(static oracle => (
                    oracle.OracleId,
                    oracle.Required,
                    oracle.Independent,
                    Source: "businessOracles")))
            .ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var requiredIndependentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (string.IsNullOrWhiteSpace(declaration.OracleId))
            {
                result.Errors.Add($"{declaration.Source} entries require an oracleId.");
                continue;
            }
            if (!ids.Add(declaration.OracleId))
                result.Errors.Add($"oracleId '{declaration.OracleId}' is duplicated.");
            if (declaration.Source == "independentBusinessOracles" &&
                !declaration.Independent)
            {
                result.Errors.Add(
                    $"independentBusinessOracles entry '{declaration.OracleId}' must set independent to true.");
            }
            if (declaration.Required && !declaration.Independent)
            {
                result.Errors.Add(
                    $"required business oracle '{declaration.OracleId}' must set independent to true.");
            }
            if (declaration.Required && declaration.Independent)
                requiredIndependentIds.Add(declaration.OracleId);
        }

        foreach (var criterion in acceptanceCriteria.Where(static criterion =>
                     criterion is not null &&
                     criterion.Required &&
                     !string.IsNullOrWhiteSpace(criterion.BusinessOracleId)))
        {
            if (!requiredIndependentIds.Contains(criterion.BusinessOracleId!))
            {
                result.Errors.Add(
                    $"required acceptance criterion '{criterion.CriterionId ?? "(unnamed)"}' references an oracle that is not required and independent.");
            }
        }
    }
}
