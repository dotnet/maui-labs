using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public sealed record CompositionValidationError(string Code, string Path, string Message);

public sealed record CompositionValidationResult(IReadOnlyList<CompositionValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class CompositionPlanValidator(GenerativeUiRegistry registry)
{
    public CompositionValidationResult Validate(
        CompositionPlan plan,
        string expectedScaffold,
        IReadOnlyList<ResolvedComponentCandidate> candidates,
        CompositionPlan? currentPlan = null,
        string? expectedPlanId = null,
        int? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedScaffold);
        ArgumentNullException.ThrowIfNull(candidates);

        var errors = new List<CompositionValidationError>();
        AddPlanErrors(plan, expectedScaffold, currentPlan, errors);
        if (expectedPlanId is not null &&
            !string.Equals(plan.PlanId, expectedPlanId, StringComparison.Ordinal))
        {
            errors.Add(new(
                "unexpected_plan_id",
                "$.planId",
                $"Expected planId '{expectedPlanId}'."));
        }
        if (expectedRevision is not null && plan.Revision != expectedRevision)
        {
            errors.Add(new(
                "unexpected_revision",
                "$.revision",
                $"Expected revision {expectedRevision}."));
        }

        var scaffold = registry.GetScaffold(plan.Scaffold);
        if (scaffold is null)
        {
            errors.Add(new(
                "unknown_scaffold",
                "$.scaffold",
                $"Scaffold '{plan.Scaffold}' is not registered."));
        }

        var candidatesByAlias = candidates.ToDictionary(
            candidate => candidate.Descriptor.Alias,
            StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var slotCounts = new Dictionary<CompositionSlot, int>();

        for (var index = 0; index < plan.Sections.Count; index++)
        {
            var section = plan.Sections[index];
            var path = $"$.sections[{index}]";

            if (string.IsNullOrWhiteSpace(section.Id))
            {
                errors.Add(new("missing_section_id", $"{path}.id", "Section id is required."));
            }
            else if (!seenIds.Add(section.Id))
            {
                errors.Add(new(
                    "duplicate_section_id",
                    $"{path}.id",
                    $"Section id '{section.Id}' is duplicated."));
            }

            if (string.IsNullOrWhiteSpace(section.Reason))
                errors.Add(new("missing_reason", $"{path}.reason", "Section reason is required."));

            var registration = registry.GetComponent(section.Component);
            if (registration is null)
            {
                errors.Add(new(
                    "unknown_component",
                    $"{path}.component",
                    $"Component '{section.Component}' is not registered."));
                continue;
            }

            if (!candidatesByAlias.TryGetValue(registration.Descriptor.Alias, out var candidate))
            {
                errors.Add(new(
                    "incompatible_component",
                    $"{path}.component",
                    $"Component '{section.Component}' is not compatible with the available data."));
                continue;
            }

            if (!string.Equals(section.DataPath, candidate.DataPath, StringComparison.Ordinal))
            {
                errors.Add(new(
                    "incompatible_data_path",
                    $"{path}.dataPath",
                    $"Component '{section.Component}' must use dataPath '{candidate.DataPath}'."));
            }

            if (!candidate.Descriptor.AllowedSlots.Contains(section.Slot))
            {
                errors.Add(new(
                    "component_slot_not_allowed",
                    $"{path}.slot",
                    $"Component '{section.Component}' is not allowed in slot '{section.Slot}'."));
            }

            if (scaffold is not null && scaffold.Slots.All(slot => slot.Slot != section.Slot))
            {
                errors.Add(new(
                    "unknown_slot",
                    $"{path}.slot",
                    $"Scaffold '{scaffold.Name}' does not define slot '{section.Slot}'."));
            }

            if (section.Variant is not null &&
                !candidate.Descriptor.Variants.Contains(section.Variant, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new(
                    "unknown_variant",
                    $"{path}.variant",
                    $"Component '{section.Component}' does not define variant '{section.Variant}'."));
            }

            slotCounts[section.Slot] = slotCounts.GetValueOrDefault(section.Slot) + 1;
        }

        if (scaffold is not null)
        {
            foreach (var slot in scaffold.Slots.Where(slot => !slot.AllowsMultiple))
            {
                if (slotCounts.GetValueOrDefault(slot.Slot) > 1)
                {
                    errors.Add(new(
                        "slot_capacity_exceeded",
                        "$.sections",
                        $"Scaffold slot '{slot.Slot}' accepts only one section."));
                }
            }
        }

        AddContinuityErrors(plan, currentPlan, errors);
        return new CompositionValidationResult(errors);
    }

    private static void AddPlanErrors(
        CompositionPlan plan,
        string expectedScaffold,
        CompositionPlan? currentPlan,
        ICollection<CompositionValidationError> errors)
    {
        if (plan.SchemaVersion != CompositionPlan.CurrentSchemaVersion)
        {
            errors.Add(new(
                "unsupported_schema_version",
                "$.schemaVersion",
                $"Expected schemaVersion {CompositionPlan.CurrentSchemaVersion}."));
        }

        if (string.IsNullOrWhiteSpace(plan.PlanId))
            errors.Add(new("missing_plan_id", "$.planId", "Plan id is required."));

        if (plan.Revision < 1)
            errors.Add(new("invalid_revision", "$.revision", "Revision must be at least 1."));

        if (!string.Equals(plan.Scaffold, expectedScaffold, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(
                "unexpected_scaffold",
                "$.scaffold",
                $"Expected scaffold '{expectedScaffold}'."));
        }

        if (string.IsNullOrWhiteSpace(plan.Title))
            errors.Add(new("missing_title", "$.title", "Plan title is required."));

        if (plan.Sections.Count == 0)
            errors.Add(new("empty_sections", "$.sections", "At least one section is required."));

        if (currentPlan is null)
            return;

        if (!string.Equals(plan.PlanId, currentPlan.PlanId, StringComparison.Ordinal))
        {
            errors.Add(new(
                "plan_id_changed",
                "$.planId",
                $"Follow-up plan must preserve planId '{currentPlan.PlanId}'."));
        }

        if (plan.Revision != currentPlan.Revision + 1)
        {
            errors.Add(new(
                "unexpected_revision",
                "$.revision",
                $"Follow-up revision must be {currentPlan.Revision + 1}."));
        }
    }

    private static void AddContinuityErrors(
        CompositionPlan plan,
        CompositionPlan? currentPlan,
        ICollection<CompositionValidationError> errors)
    {
        if (currentPlan is null)
            return;

        var priorStableSections = currentPlan.Sections
            .GroupBy(
                section => (section.Component.ToUpperInvariant(), section.DataPath),
                section => section)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

        for (var index = 0; index < plan.Sections.Count; index++)
        {
            var section = plan.Sections[index];
            var key = (section.Component.ToUpperInvariant(), section.DataPath);
            if (priorStableSections.TryGetValue(key, out var prior) &&
                !string.Equals(section.Id, prior.Id, StringComparison.Ordinal))
            {
                errors.Add(new(
                    "unstable_section_id",
                    $"$.sections[{index}].id",
                    $"Component '{section.Component}' must preserve section id '{prior.Id}'."));
            }
        }
    }
}

public static class CompositionValidationErrorFormatter
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public static string Format(CompositionValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var issues = new JsonArray();
        foreach (var error in result.Errors
                     .OrderBy(error => error.Path, StringComparer.Ordinal)
                     .ThenBy(error => error.Code, StringComparer.Ordinal))
        {
            issues.Add(new JsonObject
            {
                ["code"] = error.Code,
                ["path"] = error.Path,
                ["message"] = error.Message,
            });
        }

        return new JsonObject
        {
            ["error"] = "invalid_composition_plan",
            ["issues"] = issues,
        }.ToJsonString(s_options);
    }
}

public sealed record CompositionFallbackContext(
    string Scaffold,
    string DataPath,
    string Title,
    string PlanId,
    int Revision,
    CompositionPlan? CurrentPlan = null);

public interface ICompositionFallbackPlanFactory
{
    string Scaffold { get; }

    CompositionPlan CreateFallback(CompositionFallbackContext context);
}
