// Ported from microsoft/microsoft-ui-reactor @7c90d29 (tests/Reactor.Tests/YogaGenerated/YogaAutoTest.cs).
// Upstream licence: MIT (Microsoft Corporation). Original fixtures: Meta's Yoga (MIT).

using Comet.Layout.Yoga;
using Comet.Layout.Yoga;
using Xunit;

namespace Comet.Layout.Yoga.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGAutoTest.cpp
/// </summary>
public class YogaAutoTest
{
    [Fact]
    public void Auto_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Auto;
        root.Height = YogaValue.Point(50f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(50f);
        root_child2.Height = YogaValue.Point(50f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(150f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(100f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(150f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Auto_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(50f);
        root.Height = YogaValue.Auto;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(50f);
        root_child2.Height = YogaValue.Point(50f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(100f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(100f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Auto_Flex_Basis()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(50f);
        root.FlexBasis = YogaValue.Auto;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(50f);
        root_child2.Height = YogaValue.Point(50f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(100f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(150f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(100f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Auto_Position()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(50f);
        root.Height = YogaValue.Point(50f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(25f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.SetPosition(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(25f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Auto_Margin()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(50f);
        root.Height = YogaValue.Point(50f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(25f);
        root_child0.Height = YogaValue.Point(25f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(25f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(50f, root.LayoutWidth);
        Assert.Equal(50f, root.LayoutHeight);
        Assert.Equal(25f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(25f, root_child0.LayoutHeight);
    }

}