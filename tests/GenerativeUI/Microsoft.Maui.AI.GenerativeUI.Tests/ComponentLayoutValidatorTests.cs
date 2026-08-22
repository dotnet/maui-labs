using System.Text.Json;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Tests;

public sealed class ComponentLayoutValidatorTests
{
    [Fact]
    public void Serialize_FlatLayout_UsesEnumStringsAndNoRecursiveChildren()
    {
        var json = JsonSerializer.Serialize(
            AdaptiveCompositionTestCatalog.StandardLayout(),
            ComponentLayoutJsonContext.Default.ComponentLayoutDocument);

        Assert.Contains("\"kind\": \"Stack\"", json, StringComparison.Ordinal);
        Assert.Contains("\"orientation\": \"Vertical\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"children\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_StandardLayout_IsValid()
    {
        var result = new ComponentLayoutValidator().Validate(
            AdaptiveCompositionTestCatalog.StandardLayout(),
            AdaptiveCompositionTestCatalog.Context());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_UnknownComponentOrPrimitiveLikeLeaf_ReturnsStructuredErrors()
    {
        var layout = AdaptiveCompositionTestCatalog.StandardLayout() with
        {
            Nodes =
            [
                AdaptiveCompositionTestCatalog.StandardLayout().Nodes[0],
                AdaptiveCompositionTestCatalog.StandardLayout().Nodes[1] with
                {
                    Component = "Label",
                    Title = "Model-authored primitive",
                },
            ],
        };

        var result = new ComponentLayoutValidator().Validate(
            layout,
            AdaptiveCompositionTestCatalog.Context());

        Assert.Contains(result.Errors, issue => issue.Code == "unknown_component");
        Assert.Contains(result.Errors, issue => issue.Code == "component_layout_properties");
    }

    [Fact]
    public void Validate_HarmlessIdRename_IsWarningAndRemainsValid()
    {
        var current = AdaptiveCompositionTestCatalog.StandardLayout();
        var followUp = AdaptiveCompositionTestCatalog.StandardLayout(
            revision: 2,
            rootId: "renamed-root",
            componentId: "renamed-hero");

        var result = new ComponentLayoutValidator().Validate(
            followUp,
            AdaptiveCompositionTestCatalog.Context(),
            current);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, issue => issue.Code == "unstable_node_id");
    }

    [Fact]
    public void Formatter_OrdersIssuesDeterministically()
    {
        var formatted = ComponentLayoutValidationErrorFormatter.Format(
            new(
            [
                new("z", "$.nodes[1]", "Second."),
                new("a", "$.nodes[0]", "First."),
            ]));

        Assert.True(
            formatted.IndexOf("\"code\": \"a\"", StringComparison.Ordinal) <
            formatted.IndexOf("\"code\": \"z\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MultipleRegionsMayUseRootOrderZero()
    {
        var context = AdaptiveCompositionTestCatalog.Context() with
        {
            Surface = AdaptiveCompositionTestCatalog.Context().Surface with
            {
                Regions =
                [
                    new() { Name = "Primary", Description = "Primary." },
                    new() { Name = "Secondary", Description = "Secondary." },
                ],
            },
            ComponentCatalog =
            [
                AdaptiveCompositionTestCatalog.Context().ComponentCatalog[0] with
                {
                    AllowedRegions = ["Primary", "Secondary"],
                },
            ],
        };
        var layout = AdaptiveCompositionTestCatalog.StandardLayout() with
        {
            Regions =
            [
                new() { Region = "Primary", RootNodeId = "primary-root" },
                new() { Region = "Secondary", RootNodeId = "secondary-root" },
            ],
            Nodes =
            [
                AdaptiveCompositionTestCatalog.StandardLayout().Nodes[0] with { Id = "primary-root" },
                AdaptiveCompositionTestCatalog.StandardLayout().Nodes[1] with
                {
                    Id = "primary-component",
                    ParentId = "primary-root",
                },
                AdaptiveCompositionTestCatalog.StandardLayout().Nodes[0] with { Id = "secondary-root" },
                AdaptiveCompositionTestCatalog.StandardLayout().Nodes[1] with
                {
                    Id = "secondary-component",
                    ParentId = "secondary-root",
                },
            ],
        };

        var result = new ComponentLayoutValidator().Validate(layout, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateRegionAndUndefinedEnums_ReturnsErrors()
    {
        var standard = AdaptiveCompositionTestCatalog.StandardLayout();
        var layout = standard with
        {
            Regions =
            [
                standard.Regions[0],
                standard.Regions[0],
            ],
            Nodes =
            [
                standard.Nodes[0] with
                {
                    Kind = (ComponentLayoutNodeKind)99,
                    Orientation = (AdaptiveStackOrientation)99,
                },
                standard.Nodes[1],
            ],
        };

        var result = new ComponentLayoutValidator().Validate(
            layout,
            AdaptiveCompositionTestCatalog.Context());

        Assert.Contains(result.Errors, issue => issue.Code == "duplicate_region");
        Assert.Contains(result.Errors, issue => issue.Code == "unknown_node_kind");
        Assert.Contains(result.Errors, issue => issue.Code == "unknown_orientation");
    }

    [Fact]
    public void Validate_NullTableEntries_ReturnsStructuredErrors()
    {
        var standard = AdaptiveCompositionTestCatalog.StandardLayout();
        var layout = standard with
        {
            Regions = [null!],
            Nodes = [null!],
        };

        var result = new ComponentLayoutValidator().Validate(
            layout,
            AdaptiveCompositionTestCatalog.Context());

        Assert.Contains(result.Errors, issue => issue.Code == "null_region");
        Assert.Contains(result.Errors, issue => issue.Code == "null_node");
    }

    [Fact]
    public void Validate_OmittedVariant_IsValidWhenComponentHasNoNamedVariants()
    {
        var context = AdaptiveCompositionTestCatalog.Context(
        [
            AdaptiveCompositionTestCatalog.Context().ComponentCatalog[0] with
            {
                Variants = [],
            },
        ]);
        var standard = AdaptiveCompositionTestCatalog.StandardLayout();
        var layout = standard with
        {
            Nodes =
            [
                standard.Nodes[0],
                standard.Nodes[1] with { Variant = null },
            ],
        };

        Assert.True(new ComponentLayoutValidator().Validate(layout, context).IsValid);
    }
}
