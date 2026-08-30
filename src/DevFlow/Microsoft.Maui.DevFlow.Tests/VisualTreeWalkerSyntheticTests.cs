using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class VisualTreeWalkerSyntheticTests
{
    [Fact]
    public void ToolbarItemId_IsStableAndIncludesAutomationId()
    {
        var item = new ToolbarItem
        {
            AutomationId = "ToolbarAction1",
            Text = "Action1"
        };

        var first = VisualTreeWalker.GetToolbarItemId(item);
        var second = VisualTreeWalker.GetToolbarItemId(item);

        Assert.Equal(first, second);
        Assert.StartsWith("ToolbarAction1_", first);
    }

    [Fact]
    public void ContainerFallbacks_AreUsedByTreeQueryAndElementLookup()
    {
        var flyoutLabel = new Label { AutomationId = "FlyoutLabel", Text = "Menu" };
        var detailLabel = new Label { AutomationId = "DetailLabel", Text = "Detail" };
        var navigationPage = new NavigationPage(
            new ContentPage
            {
                Content = detailLabel,
            });
        var flyoutPage = new FlyoutPage
        {
            Flyout = new ContentPage
            {
                Title = "Menu",
                Content = flyoutLabel,
            },
            Detail = navigationPage,
            IsPresented = true,
        };
        var app = new TestApplication([flyoutPage]);
        var walker = new VisualTreeWalker();

        var tree = walker.WalkTree(app);
        var elements = VisualTreeWalker.FlattenElementInfos(tree).ToList();

        Assert.Contains(elements, element => element.AutomationId == "FlyoutLabel");
        Assert.Contains(elements, element => element.AutomationId == "DetailLabel");
        Assert.Single(walker.Query(app, automationId: "FlyoutToggle"));
        Assert.Single(walker.Query(app, automationId: "FlyoutLabel"));
        Assert.Same(detailLabel, walker.GetElementById("DetailLabel", app));
    }

    [Fact]
    public void HitTestCandidates_PromoteDescendantWithoutCrossingUnrelatedPlatformHit()
    {
        var button = new Button();
        var layout = new Grid { Children = { button } };
        var overlay = new Grid();

        var ordered = DevFlowAgentService.MergeHitTestCandidates(
            [overlay, layout],
            [layout, button],
            element => ReferenceEquals(element, button)
                ? new BoundsInfo { Width = 100, Height = 40 }
                : new BoundsInfo { Width = 500, Height = 400 });

        Assert.Same(overlay, ordered[0]);
        Assert.Same(button, ordered[1]);
        Assert.Same(layout, ordered[2]);
    }

    [Fact]
    public void StableItemKey_AttachedProperty_IsIncludedInElementInfo()
    {
        var button = new Button { AutomationId = "RepeatedAction" };
        DevFlowTest.SetStableItemKey(button, "item-42");
        var page = new ContentPage { Content = button };
        var app = new TestApplication([page]);

        var elements = VisualTreeWalker.FlattenElementInfos(new VisualTreeWalker().WalkTree(app)).ToList();

        Assert.Contains(elements, element =>
            element.AutomationId == "RepeatedAction" &&
            FlowSelector.IsOpaqueStableItemKey(element.StableItemKey) &&
            element.StableItemKey != "item-42");
    }

    private sealed class TestApplication(IEnumerable<IVisualTreeElement> children)
        : Application, IVisualTreeElement
    {
        private readonly IReadOnlyList<IVisualTreeElement> _children = children.ToArray();

        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }
}
