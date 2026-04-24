using System;
using Comet.Layout;
using Comet.Layout.Yoga;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using Xunit;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexAlign = Comet.Layout.Yoga.FlexAlign;

namespace Comet.Tests.Layout
{
	public class YogaMeasureBridgeTests : TestBase
	{
		// Helper: build a plain Comet.View whose handler reports a fixed intrinsic size.
		static View MakeLeaf(Size intrinsic)
		{
			var view = new View();
			InitializeHandlers(view);
			var handler = (GenericViewHandler)view.ViewHandler;
			handler.OnGetIntrinsicSize = (_, _) => intrinsic;
			return view;
		}

		[Fact]
		public void FrameConstraints_Width_And_Height_Are_Applied_As_Points()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.Frame(width: 120, height: 80);

			var node = YogaMeasureBridge.CreateLeafNode(view);

			Assert.True(node.Width.IsPoint);
			Assert.Equal(120f, node.Width.Value);
			Assert.True(node.Height.IsPoint);
			Assert.Equal(80f, node.Height.Value);
		}

		[Fact]
		public void Missing_FrameConstraints_Leaves_Auto()
		{
			var view = MakeLeaf(new Size(10, 10));

			var node = YogaMeasureBridge.CreateLeafNode(view);

			Assert.True(node.Width.IsAuto);
			Assert.True(node.Height.IsAuto);
		}

		[Fact]
		public void Margin_Is_Projected_To_All_Four_Edges()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.Margin(new Thickness(1, 2, 3, 4));

			var node = YogaMeasureBridge.CreateLeafNode(view);

			Assert.Equal(1f, node.Style.Margin[(int)YogaEdge.Left].Value);
			Assert.Equal(2f, node.Style.Margin[(int)YogaEdge.Top].Value);
			Assert.Equal(3f, node.Style.Margin[(int)YogaEdge.Right].Value);
			Assert.Equal(4f, node.Style.Margin[(int)YogaEdge.Bottom].Value);
		}

		[Fact]
		public void FillHorizontal_In_Column_Parent_Maps_To_AlignSelf_Stretch()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.FillHorizontal();

			var node = YogaMeasureBridge.CreateLeafNode(view, parentDirection: YogaFlexDirection.Column);

			Assert.Equal(YogaFlexAlign.Stretch, node.AlignSelf);
			Assert.Equal(0f, node.FlexGrow);
		}

		[Fact]
		public void FillVertical_In_Column_Parent_Is_MainAxis_Grow()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.FillVertical();

			var node = YogaMeasureBridge.CreateLeafNode(view, parentDirection: YogaFlexDirection.Column);

			Assert.Equal(1f, node.FlexGrow);
		}

		[Fact]
		public void FillVertical_In_Row_Parent_Maps_To_AlignSelf_Stretch()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.FillVertical();

			var node = YogaMeasureBridge.CreateLeafNode(view, parentDirection: YogaFlexDirection.Row);

			Assert.Equal(YogaFlexAlign.Stretch, node.AlignSelf);
			Assert.Equal(0f, node.FlexGrow);
		}

		[Fact]
		public void Center_Alignment_Maps_To_AlignSelf_Center()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.HorizontalLayoutAlignment(LayoutAlignment.Center);

			var node = YogaMeasureBridge.CreateLeafNode(view, parentDirection: YogaFlexDirection.Column);

			Assert.Equal(YogaFlexAlign.Center, node.AlignSelf);
		}

		[Fact]
		public void Flex_Basis_Grow_Shrink_Propagate()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.FlexBasis(50);
			view.FlexGrow(2);
			view.FlexShrink(3);

			var node = YogaMeasureBridge.CreateLeafNode(view);

			Assert.True(node.FlexBasis.IsPoint);
			Assert.Equal(50f, node.FlexBasis.Value);
			Assert.Equal(2f, node.FlexGrow);
			Assert.Equal(3f, node.FlexShrink);
		}

		[Fact]
		public void Negative_FlexBasis_Stays_Auto()
		{
			var view = MakeLeaf(new Size(10, 10));
			// Default GetFlexBasis() returns -1 when not set.

			var node = YogaMeasureBridge.CreateLeafNode(view);

			Assert.True(node.FlexBasis.IsAuto);
		}

		[Fact]
		public void Explicit_FlexAlignSelf_Overrides_LayoutAlignment_Derived()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.HorizontalLayoutAlignment(LayoutAlignment.Center);
			view.FlexAlignSelf(YogaFlexAlign.FlexEnd);

			var node = YogaMeasureBridge.CreateLeafNode(view, parentDirection: YogaFlexDirection.Column);

			Assert.Equal(YogaFlexAlign.FlexEnd, node.AlignSelf);
		}

		[Fact]
		public void MeasureFunction_Invokes_View_Measure_And_Returns_Result()
		{
			var view = MakeLeaf(new Size(77, 33));

			var node = YogaMeasureBridge.CreateLeafNode(view);

			Assert.True(node.HasMeasureFunc);
			var size = node.MeasureFunction!(node, 200, YogaMeasureMode.AtMost, 200, YogaMeasureMode.AtMost);
			Assert.Equal(77f, size.Width);
			Assert.Equal(33f, size.Height);
		}

		[Fact]
		public void Full_Calculation_Plus_ArrangeFromNode_Writes_Frame()
		{
			var view = MakeLeaf(new Size(40, 20));
			view.Frame(width: 50, height: 25);

			var root = new YogaNode
			{
				FlexDirection = YogaFlexDirection.Column,
				Width = YogaValue.Point(200),
				Height = YogaValue.Point(100),
			};
			var leaf = YogaMeasureBridge.CreateLeafNode(view, parentDirection: YogaFlexDirection.Column);
			root.InsertChild(leaf, 0);

			root.CalculateLayout();

			YogaMeasureBridge.ArrangeFromNode(view, leaf, Point.Zero);

			Assert.Equal(new Rect(0, 0, 50, 25), view.Frame);
		}

		[Fact]
		public void ApplyStyle_Is_Idempotent()
		{
			var view = MakeLeaf(new Size(10, 10));
			view.Frame(width: 30, height: 40);

			var node = new YogaNode();
			YogaMeasureBridge.ApplyStyle(node, view);
			YogaMeasureBridge.ApplyStyle(node, view);

			Assert.Equal(30f, node.Width.Value);
			Assert.Equal(40f, node.Height.Value);
		}
	}
}
