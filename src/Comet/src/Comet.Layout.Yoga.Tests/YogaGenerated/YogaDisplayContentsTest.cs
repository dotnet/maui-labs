// Ported from microsoft/microsoft-ui-reactor @7c90d29 (tests/Reactor.Tests/YogaGenerated/YogaDisplayContentsTest.cs).
// Upstream licence: MIT (Microsoft Corporation). Original fixtures: Meta's Yoga (MIT).

using Comet.Layout.Yoga;
using Comet.Layout.Yoga;
using Xunit;

namespace Comet.Layout.Yoga.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGDisplayContentsTest.cpp
/// </summary>
public class YogaDisplayContentsTest
{
    [Fact]
    public void Test1()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.FlexDirection = FlexDirection.Row;
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Display = YogaDisplay.Contents;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.FlexGrow = 1f;
        root_child0_child0.FlexShrink = 1f;
        root_child0_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0_child0.Height = YogaValue.Point(10f);
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child1 = new YogaNode(config);
        root_child0_child1.FlexGrow = 1f;
        root_child0_child1.FlexShrink = 1f;
        root_child0_child1.FlexBasis = YogaValue.Percent(0f);
        root_child0_child1.Height = YogaValue.Point(20f);
        root_child0.InsertChild(root_child0_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child1.LayoutWidth);
        Assert.Equal(20f, root_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child1.LayoutWidth);
        Assert.Equal(20f, root_child0_child1.LayoutHeight);
    }

}