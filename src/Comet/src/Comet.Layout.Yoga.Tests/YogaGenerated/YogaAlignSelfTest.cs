// Ported from microsoft/microsoft-ui-reactor @7c90d29 (tests/Reactor.Tests/YogaGenerated/YogaAlignSelfTest.cs).
// Upstream licence: MIT (Microsoft Corporation). Original fixtures: Meta's Yoga (MIT).

using Comet.Layout.Yoga;
using Comet.Layout.Yoga;
using Xunit;

namespace Comet.Layout.Yoga.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGAlignSelfTest.cpp
/// </summary>
public class FlexAlignSelfTest
{
    [Fact]
    public void Align_Self_Center()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(10f);
        root_child0.Width = YogaValue.Point(10f);
        root_child0.AlignSelf = FlexAlign.Center;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(45f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(45f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Align_Self_Flex_End()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(10f);
        root_child0.Width = YogaValue.Point(10f);
        root_child0.AlignSelf = FlexAlign.FlexEnd;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(90f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Align_Self_Flex_Start()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(10f);
        root_child0.Width = YogaValue.Point(10f);
        root_child0.AlignSelf = FlexAlign.FlexStart;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(90f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Align_Self_Flex_End_Override_Flex_Start()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.AlignItems = FlexAlign.FlexStart;
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(10f);
        root_child0.Width = YogaValue.Point(10f);
        root_child0.AlignSelf = FlexAlign.FlexEnd;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(90f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Align_Self_Baseline()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.AlignSelf = FlexAlign.Baseline;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(20f);
        root_child1.AlignSelf = FlexAlign.Baseline;
        root.InsertChild(root_child1, 1);
        var root_child1_child0 = new YogaNode(config);
        root_child1_child0.Width = YogaValue.Point(50f);
        root_child1_child0.Height = YogaValue.Point(10f);
        root_child1.InsertChild(root_child1_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(20f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(50f, root_child1_child0.LayoutWidth);
        Assert.Equal(10f, root_child1_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(40f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(20f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(50f, root_child1_child0.LayoutWidth);
        Assert.Equal(10f, root_child1_child0.LayoutHeight);
    }

}