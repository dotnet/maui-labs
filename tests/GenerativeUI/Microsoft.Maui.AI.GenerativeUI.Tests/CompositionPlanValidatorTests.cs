using GenerativeUI.Sample.Garden.Shared;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class CompositionPlanValidatorTests
{
    [Fact]
    public void Validate_CompatiblePlan_IsValid()
    {
        var (validator, candidates) = CreateWateringCanValidator();

        var result = validator.Validate(
            CompositionTestCatalog.ValidWateringCanPlan(),
            CompositionTestCatalog.Scaffold,
            candidates);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_UnknownComponent_ReturnsStructuredError()
    {
        var (validator, candidates) = CreateWateringCanValidator();
        var plan = CompositionTestCatalog.ValidWateringCanPlan();
        plan = plan with
        {
            Sections =
            [
                plan.Sections[0] with { Component = "InventedPanel" },
                .. plan.Sections.Skip(1),
            ],
        };

        var result = validator.Validate(plan, CompositionTestCatalog.Scaffold, candidates);

        var error = Assert.Single(result.Errors, error => error.Code == "unknown_component");
        Assert.Equal("$.sections[0].component", error.Path);
    }

    [Fact]
    public void Validate_ComponentWithoutRequiredFacet_ReturnsIncompatibleError()
    {
        var registry = CompositionTestCatalog.CreateRegistry();
        var resolver = new ComponentCandidateResolver(registry);
        var candidates = resolver.Resolve(
            CompositionTestCatalog.CreateState(GardenProductFixtures.BasilSeeds),
            nameof(Product),
            CompositionTestCatalog.DataPath);
        var plan = CompositionTestCatalog.ValidWateringCanPlan() with
        {
            Title = "Basil Seeds",
            Sections =
            [
                CompositionTestCatalog.ValidWateringCanPlan().Sections[0],
                new CompositionSection
                {
                    Id = "dimensions",
                    Slot = CompositionSlot.Primary,
                    Component = "DimensionsPanel",
                    DataPath = CompositionTestCatalog.DataPath,
                    Variant = "default",
                    Priority = 80,
                    Reason = "Invalid for seed data.",
                },
            ],
        };

        var result = new CompositionPlanValidator(registry).Validate(
            plan,
            CompositionTestCatalog.Scaffold,
            candidates);

        Assert.Contains(result.Errors, error => error.Code == "incompatible_component");
    }

    [Fact]
    public void Validate_DuplicateIdsInvalidPathSlotAndVariant_ReturnsAllErrors()
    {
        var (validator, candidates) = CreateWateringCanValidator();
        var plan = CompositionTestCatalog.ValidWateringCanPlan();
        plan = plan with
        {
            Sections =
            [
                plan.Sections[0],
                plan.Sections[1] with
                {
                    Id = "hero",
                    DataPath = "other",
                    Slot = CompositionSlot.Hero,
                    Variant = "huge",
                },
            ],
        };

        var result = validator.Validate(plan, CompositionTestCatalog.Scaffold, candidates);

        Assert.Contains(result.Errors, error => error.Code == "duplicate_section_id");
        Assert.Contains(result.Errors, error => error.Code == "incompatible_data_path");
        Assert.Contains(result.Errors, error => error.Code == "component_slot_not_allowed");
        Assert.Contains(result.Errors, error => error.Code == "unknown_variant");
        Assert.Contains(result.Errors, error => error.Code == "slot_capacity_exceeded");
    }

    [Fact]
    public void Validate_FollowUpChangedIdentityAndRevision_ReturnsContinuityErrors()
    {
        var (validator, candidates) = CreateWateringCanValidator();
        var current = CompositionTestCatalog.ValidWateringCanPlan();
        var followUp = current with
        {
            PlanId = "replacement",
            Revision = 4,
            Sections =
            [
                current.Sections[0] with { Id = "new-hero" },
                .. current.Sections.Skip(1),
            ],
        };

        var result = validator.Validate(
            followUp,
            CompositionTestCatalog.Scaffold,
            candidates,
            current);

        Assert.Contains(result.Errors, error => error.Code == "plan_id_changed");
        Assert.Contains(result.Errors, error => error.Code == "unexpected_revision");
        Assert.Contains(result.Errors, error => error.Code == "unstable_section_id");
    }

    [Fact]
    public void Formatter_OrdersAndFormatsErrorsDeterministically()
    {
        var result = new CompositionValidationResult(
        [
            new("z_code", "$.sections[1]", "Second."),
            new("a_code", "$.sections[0]", "First."),
        ]);

        var formatted = CompositionValidationErrorFormatter.Format(result);

        Assert.Equal(
            """
            {
              "error": "invalid_composition_plan",
              "issues": [
                {
                  "code": "a_code",
                  "path": "$.sections[0]",
                  "message": "First."
                },
                {
                  "code": "z_code",
                  "path": "$.sections[1]",
                  "message": "Second."
                }
              ]
            }
            """,
            formatted);
    }

    private static (CompositionPlanValidator Validator, IReadOnlyList<ResolvedComponentCandidate> Candidates)
        CreateWateringCanValidator()
    {
        var registry = CompositionTestCatalog.CreateRegistry();
        var resolver = new ComponentCandidateResolver(registry);
        var candidates = resolver.Resolve(
            CompositionTestCatalog.CreateState(GardenProductFixtures.WateringCan),
            nameof(Product),
            CompositionTestCatalog.DataPath);
        return (new CompositionPlanValidator(registry), candidates);
    }
}
