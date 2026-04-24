// Ported from microsoft/microsoft-ui-reactor @7c90d29 (tests/Reactor.Tests/YogaGenerated/YogaDisplayTest.cs).
// Upstream licence: MIT (Microsoft Corporation). Original fixtures: Meta's Yoga (MIT).

using Comet.Layout.Yoga;
using Comet.Layout.Yoga;
using Xunit;

namespace Comet.Layout.Yoga.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGDisplayTest.cpp
/// </summary>
public class YogaDisplayTest
{
    [Fact]
    public void Display_None()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root_child1.Display = YogaDisplay.None;
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Display_None_Fixed_Size()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(20f);
        root_child1.Height = YogaValue.Point(20f);
        root_child1.Display = YogaDisplay.None;
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Display_None_With_Margin()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(20f);
        root_child0.Height = YogaValue.Point(20f);
        root_child0.Display = YogaDisplay.None;
        root_child0.SetMargin(YogaEdge.All, YogaValue.Point(10f));
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Display_None_With_Child()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexShrink = 1f;
        root_child0.FlexBasis = YogaValue.Percent(0f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root_child1.FlexShrink = 1f;
        root_child1.FlexBasis = YogaValue.Percent(0f);
        root_child1.Display = YogaDisplay.None;
        root.InsertChild(root_child1, 1);
        var root_child1_child0 = new YogaNode(config);
        root_child1_child0.FlexGrow = 1f;
        root_child1_child0.FlexShrink = 1f;
        root_child1_child0.FlexBasis = YogaValue.Percent(0f);
        root_child1_child0.Width = YogaValue.Point(20f);
        root_child1.InsertChild(root_child1_child0, 0);
        var root_child2 = new YogaNode(config);
        root_child2.FlexGrow = 1f;
        root_child2.FlexShrink = 1f;
        root_child2.FlexBasis = YogaValue.Percent(0f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(0f, root_child1_child0.LayoutWidth);
        Assert.Equal(0f, root_child1_child0.LayoutHeight);
        Assert.Equal(50f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(100f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(0f, root_child1_child0.LayoutWidth);
        Assert.Equal(0f, root_child1_child0.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(100f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Display_None_With_Position()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root_child1.Display = YogaDisplay.None;
        root_child1.SetPosition(YogaEdge.Top, YogaValue.Point(10f));
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Display_None_With_Position_Absolute()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Display = YogaDisplay.None;
        root_child0.PositionType = FlexPositionType.Absolute;
        root_child0.Width = YogaValue.Point(100f);
        root_child0.Height = YogaValue.Point(100f);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Display_Contents()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
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

    [Fact]
    public void Display_Contents_Fixed_Size()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Display = YogaDisplay.Contents;
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
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

    [Fact]
    public void Display_Contents_With_Margin()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(20f);
        root_child0.Height = YogaValue.Point(20f);
        root_child0.Display = YogaDisplay.Contents;
        root_child0.SetMargin(YogaEdge.All, YogaValue.Point(10f));
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.FlexGrow = 1f;
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(0f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Display_Contents_With_Padding()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Display = YogaDisplay.Contents;
        root_child0.SetPadding(YogaEdge.All, YogaValue.Point(10f));
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

    [Fact]
    public void Display_Contents_With_Position()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Display = YogaDisplay.Contents;
        root_child0.SetPosition(YogaEdge.Top, YogaValue.Point(10f));
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

    [Fact]
    public void Display_Contents_With_Position_Absolute()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Display = YogaDisplay.Contents;
        root_child0.PositionType = FlexPositionType.Absolute;
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
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

    [Fact]
    public void Display_Contents_Nested()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Display = YogaDisplay.Contents;
        root.InsertChild(root_child0, 0);
        var root_child0_child0 = new YogaNode(config);
        root_child0_child0.Display = YogaDisplay.Contents;
        root_child0.InsertChild(root_child0_child0, 0);
        var root_child0_child0_child0 = new YogaNode(config);
        root_child0_child0_child0.FlexGrow = 1f;
        root_child0_child0_child0.FlexShrink = 1f;
        root_child0_child0_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0_child0_child0.Height = YogaValue.Point(10f);
        root_child0_child0.InsertChild(root_child0_child0_child0, 0);
        var root_child0_child0_child1 = new YogaNode(config);
        root_child0_child0_child1.FlexGrow = 1f;
        root_child0_child0_child1.FlexShrink = 1f;
        root_child0_child0_child1.FlexBasis = YogaValue.Percent(0f);
        root_child0_child0_child1.Height = YogaValue.Point(20f);
        root_child0_child0.InsertChild(root_child0_child0_child1, 1);
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
        Assert.Equal(0f, root_child0_child0.LayoutWidth);
        Assert.Equal(0f, root_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child0_child1.LayoutWidth);
        Assert.Equal(20f, root_child0_child0_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
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
        Assert.Equal(0f, root_child0_child0.LayoutWidth);
        Assert.Equal(0f, root_child0_child0.LayoutHeight);
        Assert.Equal(50f, root_child0_child0_child0.LayoutX);
        Assert.Equal(0f, root_child0_child0_child0.LayoutY);
        Assert.Equal(50f, root_child0_child0_child0.LayoutWidth);
        Assert.Equal(10f, root_child0_child0_child0.LayoutHeight);
        Assert.Equal(0f, root_child0_child0_child1.LayoutX);
        Assert.Equal(0f, root_child0_child0_child1.LayoutY);
        Assert.Equal(50f, root_child0_child0_child1.LayoutWidth);
        Assert.Equal(20f, root_child0_child0_child1.LayoutHeight);
    }

    [Fact]
    public void Display_Contents_With_Siblings()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexShrink = 1f;
        root_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0.Height = YogaValue.Point(30f);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Display = YogaDisplay.Contents;
        root.InsertChild(root_child1, 1);
        var root_child1_child0 = new YogaNode(config);
        root_child1_child0.FlexGrow = 1f;
        root_child1_child0.FlexShrink = 1f;
        root_child1_child0.FlexBasis = YogaValue.Percent(0f);
        root_child1_child0.Height = YogaValue.Point(10f);
        root_child1.InsertChild(root_child1_child0, 0);
        var root_child1_child1 = new YogaNode(config);
        root_child1_child1.FlexGrow = 1f;
        root_child1_child1.FlexShrink = 1f;
        root_child1_child1.FlexBasis = YogaValue.Percent(0f);
        root_child1_child1.Height = YogaValue.Point(20f);
        root_child1.InsertChild(root_child1_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.FlexGrow = 1f;
        root_child2.FlexShrink = 1f;
        root_child2.FlexBasis = YogaValue.Percent(0f);
        root_child2.Height = YogaValue.Point(30f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(30f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
        Assert.Equal(25f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(25f, root_child1_child0.LayoutWidth);
        Assert.Equal(10f, root_child1_child0.LayoutHeight);
        Assert.Equal(50f, root_child1_child1.LayoutX);
        Assert.Equal(0f, root_child1_child1.LayoutY);
        Assert.Equal(25f, root_child1_child1.LayoutWidth);
        Assert.Equal(20f, root_child1_child1.LayoutHeight);
        Assert.Equal(75f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(30f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(25f, root_child0.LayoutWidth);
        Assert.Equal(30f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(0f, root_child1.LayoutWidth);
        Assert.Equal(0f, root_child1.LayoutHeight);
        Assert.Equal(50f, root_child1_child0.LayoutX);
        Assert.Equal(0f, root_child1_child0.LayoutY);
        Assert.Equal(25f, root_child1_child0.LayoutWidth);
        Assert.Equal(10f, root_child1_child0.LayoutHeight);
        Assert.Equal(25f, root_child1_child1.LayoutX);
        Assert.Equal(0f, root_child1_child1.LayoutY);
        Assert.Equal(25f, root_child1_child1.LayoutWidth);
        Assert.Equal(20f, root_child1_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(0f, root_child2.LayoutY);
        Assert.Equal(25f, root_child2.LayoutWidth);
        Assert.Equal(30f, root_child2.LayoutHeight);
    }

}