// Ported from microsoft/microsoft-ui-reactor @7c90d29 (tests/Reactor.Tests/YogaGenerated/YogaMarginTest.cs).
// Upstream licence: MIT (Microsoft Corporation). Original fixtures: Meta's Yoga (MIT).

using Comet.Layout.Yoga;
using Comet.Layout.Yoga;
using Xunit;

namespace Comet.Layout.Yoga.Tests.YogaGenerated;

/// <summary>
/// Ported from yoga/tests/generated/YGMarginTest.cpp
/// </summary>
public class YogaMarginTest
{
    [Fact]
    public void Margin_Start()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(10f);
        root_child0.SetMargin(YogaEdge.Start, YogaValue.Point(10f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(80f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Top()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(10f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Point(10f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_End()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        root.JustifyContent = FlexJustify.FlexEnd;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(10f);
        root_child0.SetMargin(YogaEdge.End, YogaValue.Point(10f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(80f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(10f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Bottom()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.JustifyContent = FlexJustify.FlexEnd;
        var root_child0 = new YogaNode(config);
        root_child0.Height = YogaValue.Point(10f);
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Point(10f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(80f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(80f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(10f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_And_Flex_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.SetMargin(YogaEdge.Start, YogaValue.Point(10f));
        root_child0.SetMargin(YogaEdge.End, YogaValue.Point(10f));
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(80f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(80f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_And_Flex_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Point(10f));
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Point(10f));
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(80f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(80f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_And_Stretch_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Point(10f));
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Point(10f));
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(80f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(10f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(80f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_And_Stretch_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.SetMargin(YogaEdge.Start, YogaValue.Point(10f));
        root_child0.SetMargin(YogaEdge.End, YogaValue.Point(10f));
        root_child0.FlexGrow = 1f;
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(80f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(80f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_With_Sibling_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.SetMargin(YogaEdge.End, YogaValue.Point(10f));
        root_child0.FlexGrow = 1f;
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
        Assert.Equal(45f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(55f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(45f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(55f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(45f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(45f, root_child1.LayoutWidth);
        Assert.Equal(100f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_With_Sibling_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(100f);
        root.Height = YogaValue.Point(100f);
        var root_child0 = new YogaNode(config);
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Point(10f));
        root_child0.FlexGrow = 1f;
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
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(45f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(55f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(45f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(100f, root.LayoutWidth);
        Assert.Equal(100f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(45f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(55f, root_child1.LayoutY);
        Assert.Equal(100f, root_child1.LayoutWidth);
        Assert.Equal(45f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Bottom()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Top()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(100f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(100f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Bottom_And_Top()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(50f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(50f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Bottom_And_Top_Justify_Center()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(50f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(150f, root_child0.LayoutX);
        Assert.Equal(50f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Multiple_Children_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root_child1.SetMargin(YogaEdge.Top, YogaValue.Auto);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(50f);
        root_child2.Height = YogaValue.Point(50f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(25f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(100f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(75f, root_child2.LayoutX);
        Assert.Equal(150f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(25f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(100f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(75f, root_child2.LayoutX);
        Assert.Equal(150f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Multiple_Children_Row()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root_child1.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child1, 1);
        var root_child2 = new YogaNode(config);
        root_child2.Width = YogaValue.Point(50f);
        root_child2.Height = YogaValue.Point(50f);
        root.InsertChild(root_child2, 2);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(75f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(150f, root_child2.LayoutX);
        Assert.Equal(75f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(125f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(50f, root_child1.LayoutX);
        Assert.Equal(75f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        Assert.Equal(0f, root_child2.LayoutX);
        Assert.Equal(75f, root_child2.LayoutY);
        Assert.Equal(50f, root_child2.LayoutWidth);
        Assert.Equal(50f, root_child2.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_And_Right_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child1.LayoutX);
        Assert.Equal(75f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(75f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_And_Right()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Start_And_End_Column()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Start, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.End, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child1.LayoutX);
        Assert.Equal(75f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(75f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(75f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Start_And_End()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Start, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.End, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_And_Right_Column_And_Center()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(150f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(150f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Right()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(50f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_And_Right_Stretch()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.FlexDirection = FlexDirection.Row;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(50f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(0f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Top_And_Bottom_Stretch()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(50f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(50f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(0f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(150f, root_child0.LayoutX);
        Assert.Equal(50f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(50f, root_child0.LayoutHeight);
        Assert.Equal(150f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Should_Not_Be_Part_Of_Max_Height()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(250f);
        root.Height = YogaValue.Point(250f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(100f);
        root_child0.Height = YogaValue.Point(100f);
        root_child0.MaxHeight = YogaValue.Point(100f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Point(20f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(250f, root.LayoutWidth);
        Assert.Equal(250f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(250f, root.LayoutWidth);
        Assert.Equal(250f, root.LayoutHeight);
        Assert.Equal(150f, root_child0.LayoutX);
        Assert.Equal(20f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Should_Not_Be_Part_Of_Max_Width()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(250f);
        root.Height = YogaValue.Point(250f);
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(100f);
        root_child0.Height = YogaValue.Point(100f);
        root_child0.MaxWidth = YogaValue.Point(100f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Point(20f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(250f, root.LayoutWidth);
        Assert.Equal(250f, root.LayoutHeight);
        Assert.Equal(20f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(250f, root.LayoutWidth);
        Assert.Equal(250f, root.LayoutHeight);
        Assert.Equal(150f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(100f, root_child0.LayoutWidth);
        Assert.Equal(100f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_Right_Child_Bigger_Than_Parent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Height = YogaValue.Point(52f);
        root.Width = YogaValue.Point(52f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(72f);
        root_child0.Height = YogaValue.Point(72f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(-20f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_Child_Bigger_Than_Parent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Height = YogaValue.Point(52f);
        root.Width = YogaValue.Point(52f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(72f);
        root_child0.Height = YogaValue.Point(72f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(-20f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Fix_Left_Auto_Right_Child_Bigger_Than_Parent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Height = YogaValue.Point(52f);
        root.Width = YogaValue.Point(52f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(72f);
        root_child0.Height = YogaValue.Point(72f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Point(10f));
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(10f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(-20f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_Fix_Right_Child_Bigger_Than_Parent()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Height = YogaValue.Point(52f);
        root.Width = YogaValue.Point(52f);
        root.JustifyContent = FlexJustify.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(72f);
        root_child0.Height = YogaValue.Point(72f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root_child0.SetMargin(YogaEdge.Right, YogaValue.Point(10f));
        root.InsertChild(root_child0, 0);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(0f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(52f, root.LayoutWidth);
        Assert.Equal(52f, root.LayoutHeight);
        Assert.Equal(-30f, root_child0.LayoutX);
        Assert.Equal(-10f, root_child0.LayoutY);
        Assert.Equal(72f, root_child0.LayoutWidth);
        Assert.Equal(72f, root_child0.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Top_Stretching_Child()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexShrink = 1f;
        root_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0.SetMargin(YogaEdge.Top, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(150f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(100f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(150f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Left_Stretching_Child()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.FlexGrow = 1f;
        root_child0.FlexShrink = 1f;
        root_child0.FlexBasis = YogaValue.Percent(0f);
        root_child0.SetMargin(YogaEdge.Left, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(50f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(200f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(150f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(200f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(0f, root_child0.LayoutWidth);
        Assert.Equal(150f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(50f, root_child1.LayoutHeight);
    }

    [Fact]
    public void Margin_Auto_Overflowing_Container()
    {
        var config = new YogaConfig();
        var root = new YogaNode(config);
        root.PositionType = FlexPositionType.Absolute;
        root.Width = YogaValue.Point(200f);
        root.Height = YogaValue.Point(200f);
        root.AlignItems = FlexAlign.Center;
        var root_child0 = new YogaNode(config);
        root_child0.Width = YogaValue.Point(50f);
        root_child0.Height = YogaValue.Point(150f);
        root_child0.SetMargin(YogaEdge.Bottom, YogaValue.Auto);
        root.InsertChild(root_child0, 0);
        var root_child1 = new YogaNode(config);
        root_child1.Width = YogaValue.Point(50f);
        root_child1.Height = YogaValue.Point(150f);
        root.InsertChild(root_child1, 1);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.LTR);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(150f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(150f, root_child1.LayoutHeight);
        root.CalculateLayout(float.NaN, float.NaN, FlexLayoutDirection.RTL);
        Assert.Equal(0f, root.LayoutX);
        Assert.Equal(0f, root.LayoutY);
        Assert.Equal(200f, root.LayoutWidth);
        Assert.Equal(200f, root.LayoutHeight);
        Assert.Equal(75f, root_child0.LayoutX);
        Assert.Equal(0f, root_child0.LayoutY);
        Assert.Equal(50f, root_child0.LayoutWidth);
        Assert.Equal(150f, root_child0.LayoutHeight);
        Assert.Equal(75f, root_child1.LayoutX);
        Assert.Equal(150f, root_child1.LayoutY);
        Assert.Equal(50f, root_child1.LayoutWidth);
        Assert.Equal(150f, root_child1.LayoutHeight);
    }

}