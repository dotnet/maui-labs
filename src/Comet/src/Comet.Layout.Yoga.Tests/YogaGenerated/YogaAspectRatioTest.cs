// Ported from microsoft/microsoft-ui-reactor @7c90d29 (tests/Reactor.Tests/YogaGenerated/YogaAspectRatioTest.cs).
// Upstream licence: MIT (Microsoft Corporation). Original fixtures: Meta's Yoga (MIT).

using Comet.Layout.Yoga;
using Comet.Layout.Yoga;
using Xunit;

namespace Comet.Layout.Yoga.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGAspectRatioTest.cpp
/// </summary>
public class YogaAspectRatioTest
{
    [Fact(Skip = "Skipped in upstream Yoga (GTEST_SKIP)")]
    public void Aspect_Ratio_Does_Not_Stretch_Cross_Axis_Dim()
    {
        // TODO: GTEST_SKIP();
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(300f);
        root.Height = YogaValue.Point(300f);
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexShrink = 1f;
        root_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0.Overflow = YogaOverflow.Scroll;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.FlexDirection = FlexDirection.Row;
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child0_child0 = new YogaNode(config);
        root_child0_child0_child0.FlexGrow = 2f;
        root_child0_child0_child0.FlexShrink = 1f;
        root_child0_child0_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0_child0_child0.AspectRatio = 1f;
        root_child0_child0.InsertChild(root_child0_child0_child0, 0);
        var root_child0_child0_child1 = new YogaNode(config);
        root_child0_child0_child1.Width = YogaValue.Point(5f);
        root_child0_child0.InsertChild(root_child0_child0_child1, 1);
        var root_child0_child0_child2 = new YogaNode(config);
        root_child0_child0_child2.FlexGrow = 1f;
        root_child0_child0_child2.FlexShrink = 1f;
        root_child0_child0_child2.FlexBasis = YogaValue.Percent(0f);
        root_child0_child0.InsertChild(root_child0_child0_child2, 2);
        var root_child0_child0_child2_child0 = new YogaNode(config);
        root_child0_child0_child2_child0.FlexGrow = 1f;
        root_child0_child0_child2_child0.FlexShrink = 1f;
        root_child0_child0_child2_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0_child0_child2_child0.AspectRatio = 1f;
        root_child0_child0_child2.InsertChild(root_child0_child0_child2_child0, 0);
        var root_child0_child0_child2_child0_child0 = new YogaNode(config);
        root_child0_child0_child2_child0_child0.Width = YogaValue.Point(5f);
        root_child0_child0_child2_child0.InsertChild(root_child0_child0_child2_child0_child0, 0);
        var root_child0_child0_child2_child0_child1 = new YogaNode(config);
        root_child0_child0_child2_child0_child1.FlexGrow = 1f;
        root_child0_child0_child2_child0_child1.FlexShrink = 1f;
        root_child0_child0_child2_child0_child1.FlexBasis = YogaValue.Percent(0f);
        root_child0_child0_child2_child0_child1.AspectRatio = 1f;
        root_child0_child0_child2_child0.InsertChild(root_child0_child0_child2_child0_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(300f, root_child0.LayoutWidth);
        Assert.Equal(300f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(300f, root_child0_child0.LayoutWidth);
        Assert.Equal(197f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child0.LayoutY);
        Assert.Equal(197f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(197f, root_child0_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child1.LayoutY);
        Assert.Equal(5f, root_child0_child0_child1.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child1.LayoutHeight);
        Assert.Equal(202f, root_child0_child0_child2.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2.LayoutY);
        Assert.Equal(98f, root_child0_child0_child2.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child2.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child2_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2_child0.LayoutY);
        Assert.Equal(98f, root_child0_child0_child2_child0.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child2_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child2_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2_child0_child0.LayoutY);
        Assert.Equal(5f, root_child0_child0_child2_child0_child0.LayoutWidth);
        Assert.Equal(0f, root_child0_child0_child2_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child2_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2_child0_child1.LayoutY);
        Assert.Equal(98f, root_child0_child0_child2_child0_child1.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child2_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(300f, root_child0.LayoutWidth);
        Assert.Equal(300f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0.LayoutY);
        Assert.Equal(300f, root_child0_child0.LayoutWidth);
        Assert.Equal(197f, root_child0_child0.LayoutHeight);
        Assert.Equal(103f, root_child0_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child0.LayoutY);
        Assert.Equal(197f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(98f, root_child0_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child1.LayoutY);
        Assert.Equal(5f, root_child0_child0_child1.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child1.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child2.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2.LayoutY);
        Assert.Equal(98f, root_child0_child0_child2.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child2.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child2_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2_child0.LayoutY);
        Assert.Equal(98f, root_child0_child0_child2_child0.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child2_child0.LayoutHeight);
        Assert.Equal(93f, root_child0_child0_child2_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2_child0_child0.LayoutY);
        Assert.Equal(5f, root_child0_child0_child2_child0_child0.LayoutWidth);
        Assert.Equal(0f, root_child0_child0_child2_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child2_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child2_child0_child1.LayoutY);
        Assert.Equal(98f, root_child0_child0_child2_child0_child1.LayoutWidth);
        Assert.Equal(197f, root_child0_child0_child2_child0_child1.LayoutHeight);
    }

    [Fact]
    public void Zero_Aspect_Ratio_Behaves_Like_Auto()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(300f);
        root.Height = YogaValue.Point(300f);
        var root_child0 = new YogaNode(config);
        root_child0.AspectRatio = 0f;
        root_child0.Width = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(300f, root.LayoutWidth);
        Assert.Equal(300f, root.LayoutHeight);
        Assert.Equal(250f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
    }

}