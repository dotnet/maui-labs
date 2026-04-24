using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexWrap = Comet.Layout.Yoga.FlexWrap;
using YogaFlexJustify = Comet.Layout.Yoga.FlexJustify;
using YogaFlexAlign = Comet.Layout.Yoga.FlexAlign;

namespace Comet.Tests
{
	public class LayoutTests2 : TestBase
	{
		// ---- AbsoluteLayout Tests ----

		[Fact]
		public void AbsoluteLayoutCreation()
		{
			var layout = new AbsoluteLayout
			{
				new Text("Item 1").LayoutBounds(new Rect(0, 0, 100, 50)),
				new Text("Item 2").LayoutBounds(new Rect(100, 0, 100, 50))
			};
			Assert.Equal(2, ((IList<View>)layout).Count);
		}

		[Fact]
		public void AbsoluteLayoutFlags()
		{
			var view = new Text("Test")
				.LayoutBounds(new Rect(0.5, 0.5, 1, 1))
				.LayoutFlags(Comet.AbsoluteLayoutFlags.All);

			var flags = view.GetLayoutFlags();
			Assert.Equal(Comet.AbsoluteLayoutFlags.All, flags);
		}

		[Fact]
		public void AbsoluteLayoutBounds()
		{
			var view = new Text("Test").LayoutBounds(new Rect(10, 20, 100, 50));
			var bounds = view.GetLayoutBounds();
			Assert.Equal(new Rect(10, 20, 100, 50), bounds);
		}

		// ---- FlexLayout Tests ----

		[Fact]
		public void FlexLayoutCreation()
		{
			var layout = new FlexLayout(direction: YogaFlexDirection.Row, justifyContent: YogaFlexJustify.SpaceBetween, alignItems: YogaFlexAlign.Center);
			Assert.Equal(YogaFlexDirection.Row, layout.Direction);
			Assert.Equal(YogaFlexJustify.SpaceBetween, layout.JustifyContent);
		}

		[Fact]
		public void FlexLayoutWithChildren()
		{
			var layout = new FlexLayout();
			layout.Add(new Text("Item 1").FlexGrow(1));
			layout.Add(new Text("Item 2").FlexGrow(2));
			layout.Add(new Text("Item 3").FlexShrink(0));
			Assert.Equal(3, ((IList<View>)layout).Count);
		}

		[Fact]
		public void FlexLayoutChildProperties()
		{
			var view = new Text("Test")
				.FlexBasis(100)
				.FlexGrow(2)
				.FlexShrink(0)
				.FlexOrder(3);

			Assert.Equal(100, view.GetFlexBasis());
			Assert.Equal(2, view.GetFlexGrow());
			Assert.Equal(0, view.GetFlexShrink());
			Assert.Equal(3, view.GetFlexOrder());
		}

		[Fact]
		public void FlexLayoutEnums()
		{
			// Yoga enum values (upstream Meta Yoga encoding).
			Assert.Equal(2, (int)YogaFlexDirection.Row);
			Assert.Equal(0, (int)YogaFlexWrap.NoWrap);
			Assert.Equal(1, (int)YogaFlexJustify.FlexStart);
			Assert.Equal(4, (int)YogaFlexAlign.Stretch);
		}
	}
}
