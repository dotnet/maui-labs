using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class ViewExtensionTests : TestBase
	{
		// ---- IsVisible / Visibility ----

		[Fact]
		public void IsVisibleDefault()
		{
			var view = new Text("Hello");
			Assert.Equal(Visibility.Visible, ((IView)view).Visibility);
		}

		[Fact]
		public void IsVisibleSetToFalse()
		{
			var view = new Text("Hello").IsVisible(false);
			Assert.Equal(Visibility.Collapsed, ((IView)view).Visibility);
		}

		[Fact]
		public void IsVisibleSetToTrue()
		{
			var view = new Text("Hello").IsVisible(false).IsVisible(true);
			Assert.Equal(Visibility.Visible, ((IView)view).Visibility);
		}

		// ---- InputTransparent ----

		[Fact]
		public void InputTransparentDefault()
		{
			var view = new Text("Hello");
			Assert.False(((IView)view).InputTransparent);
		}

		[Fact]
		public void InputTransparentSetTrue()
		{
			var view = new Text("Hello").InputTransparent(true);
			Assert.True(((IView)view).InputTransparent);
		}

		// ---- ZIndex ----

		[Fact]
		public void ZIndexDefault()
		{
			var view = new Text("Hello");
			Assert.Equal(0, ((IView)view).ZIndex);
		}

		[Fact]
		public void ZIndexSet()
		{
			var view = new Text("Hello").ZIndex(5);
			Assert.Equal(5, ((IView)view).ZIndex);
		}

		// ---- IsEnabled ----

		[Fact]
		public void IsEnabledDefault()
		{
			var view = new Text("Hello");
			Assert.True(((IView)view).IsEnabled);
		}

		[Fact]
		public void IsEnabledSetFalse()
		{
			var view = new Text("Hello").IsEnabled(false);
			Assert.False(((IView)view).IsEnabled);
		}

		// ---- FlowDirection ----

		[Fact]
		public void FlowDirectionDefault()
		{
			var view = new Text("Hello");
			// Default is MatchParent (enum value 0) when not set — IView.FlowDirection
			// is read from environment via GetEnvironment<FlowDirection>(...) which
			// returns default(FlowDirection) (= MatchParent) when no value is set.
			Assert.Equal(FlowDirection.MatchParent, ((IView)view).FlowDirection);
		}

		[Fact]
		public void FlowDirectionRTL()
		{
			var view = new Text("Hello").FlowDirection(FlowDirection.RightToLeft);
			Assert.Equal(FlowDirection.RightToLeft, ((IView)view).FlowDirection);
		}

		// ---- Min/Max Width/Height ----

		[Fact]
		public void MinMaxDimensions()
		{
			var view = new Text("Hello")
				.MinimumWidth(50)
				.MinimumHeight(30)
				.MaximumWidth(200)
				.MaximumHeight(100);

			Assert.Equal(50, ((IView)view).MinimumWidth);
			Assert.Equal(30, ((IView)view).MinimumHeight);
			Assert.Equal(200, ((IView)view).MaximumWidth);
			Assert.Equal(100, ((IView)view).MaximumHeight);
		}

		// ---- AutomationId ----

		[Fact]
		public void AutomationIdReturnsSameInstance()
		{
			var view = new Text("Hello");
			var result = view.AutomationId("test-id");
			Assert.Same(view, result);
		}

		[Fact]
		public void AutomationIdSetsValue()
		{
			var view = new Text("Hello").AutomationId("my-automation-id");
			Assert.Equal("my-automation-id", view.GetAutomationId());
		}

		[Fact]
		public void AutomationIdChainsWithOtherExtensions()
		{
			var view = new Text("Hello")
				.AutomationId("chained-id")
				.Opacity(0.5)
				.IsVisible(true);
			Assert.Equal("chained-id", view.GetAutomationId());
			Assert.Equal(0.5, ((IView)view).Opacity);
		}

		[Fact]
		public void AutomationIdPreservesConcreteType()
		{
			// Verify the generic constraint preserves the concrete type
			Text textView = new Text("Hello").AutomationId("typed-id");
			Assert.Equal("typed-id", textView.GetAutomationId());
		}

		// ---- Opacity ----

		[Fact]
		public void OpacitySet()
		{
			var view = new Text("Hello").Opacity(0.5);
			Assert.Equal(0.5, ((IView)view).Opacity);
		}

		// ---- Scale ----

		[Fact]
		public void ScaleSet()
		{
			var view = new Text("Hello").Scale(2.0);
			Assert.Equal(2.0, ((IView)view).Scale);
		}

		// ---- Rotation ----

		[Fact]
		public void RotationSet()
		{
			var view = new Text("Hello").Rotation(45);
			Assert.Equal(45, ((IView)view).Rotation);
		}

		// ---- Translation ----

		[Fact]
		public void TranslationSet()
		{
			var view = new Text("Hello").TranslationX(10).TranslationY(20);
			Assert.Equal(10, ((IView)view).TranslationX);
			Assert.Equal(20, ((IView)view).TranslationY);
		}
	}
}
