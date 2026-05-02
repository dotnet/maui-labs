using System;
using Comet.Tests.Handlers;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class AccessibilityTests : TestBase
	{
		[Fact]
		public void SemanticDescriptionSetsDescription()
		{
			var view = new Text("Hello").SemanticDescription("A greeting label");

			var semantics = ((Microsoft.Maui.IView)view).Semantics;
			Assert.NotNull(semantics);
			Assert.Equal("A greeting label", semantics.Description);
		}

		[Fact]
		public void SemanticHintSetsHint()
		{
			var view = new Text("Hello").SemanticHint("Double tap to activate");

			var semantics = ((Microsoft.Maui.IView)view).Semantics;
			Assert.NotNull(semantics);
			Assert.Equal("Double tap to activate", semantics.Hint);
		}

		[Fact]
		public void SemanticHeadingLevelSetsHeading()
		{
			var view = new Text("Title").SemanticHeadingLevel(Microsoft.Maui.SemanticHeadingLevel.Level1);

			var semantics = ((Microsoft.Maui.IView)view).Semantics;
			Assert.NotNull(semantics);
			Assert.Equal(Microsoft.Maui.SemanticHeadingLevel.Level1, semantics.HeadingLevel);
		}

		[Fact]
		public void SemanticPropertiesCanBeChained()
		{
			var view = new Text("Hello")
				.SemanticDescription("Description")
				.SemanticHint("Hint")
				.SemanticHeadingLevel(Microsoft.Maui.SemanticHeadingLevel.Level2);

			var semantics = ((Microsoft.Maui.IView)view).Semantics;
			Assert.NotNull(semantics);
			Assert.Equal("Description", semantics.Description);
			Assert.Equal("Hint", semantics.Hint);
			Assert.Equal(Microsoft.Maui.SemanticHeadingLevel.Level2, semantics.HeadingLevel);
		}

		[Fact]
		public void SetAutomationIdSetsValue()
		{
			var view = new Text("Hello");
			view.SetAutomationId("myTextId");

			Assert.Equal("myTextId", view.GetAutomationId());
		}

		[Fact]
		public void GetAutomationIdReturnsNullWhenNotSet()
		{
			var view = new Text("Hello");
			Assert.Null(view.GetAutomationId());
		}

		[Fact]
		public void AutomationIdViaIView()
		{
			var view = new Text("Hello");
			view.SetAutomationId("testId");

			Microsoft.Maui.IView iview = view;
			Assert.Equal("testId", iview.AutomationId);
		}

		[Fact]
		public void SetAutomationIdAlsoUpdatesAccessibilityId()
		{
			var view = new Text("Hello");
			view.SetAutomationId("inspect-id");

			Assert.Equal("inspect-id", view.AccessibilityId);
			Assert.Equal("inspect-id", view.AutomationId);
		}

		[Fact]
		public void PublicInspectionPropertiesMirrorViewState()
		{
			var view = new Text("Hello");
			view.SetAutomationId("counter-increment-button");
			view.IsVisible(false);
			view.IsEnabled(false);
			view.Frame = new Rect(10, 20, 30, 40);

			Assert.Equal("counter-increment-button", view.AutomationId);
			Assert.False(view.IsVisible);
			Assert.True(view.Hidden);
			Assert.True(view.Disabled);
			Assert.Equal(Microsoft.Maui.Visibility.Collapsed, view.Visibility);
			Assert.Equal(view.Frame, view.Bounds);
			Assert.Equal(view.Frame, view.WindowBounds);
			Assert.Equal("counter-increment-button", ((Microsoft.Maui.IView)view).AutomationId);
			Assert.False(((Microsoft.Maui.IView)view).IsEnabled);
			Assert.Equal(Microsoft.Maui.Visibility.Collapsed, ((Microsoft.Maui.IView)view).Visibility);
		}

		[Fact]
		public void InspectionHandlerBridgeExposesNativeType()
		{
			var view = new Text("Hello");
			var handler = new GenericViewHandler();
			view.ViewHandler = handler;

			Assert.Same(handler, view.Handler);
			Assert.Same(view, view.PlatformView);
			Assert.Same(view, view.NativeView);
			Assert.Equal(typeof(Text).FullName, view.NativeType);
		}

		[Fact]
		public void IsReadOnlySetsProperty()
		{
			var view = new Text("Hello").IsReadOnly();

			var isReadOnly = view.GetEnvironment<bool>("View.IsReadOnly");
			Assert.True(isReadOnly);
		}

		[Fact]
		public void IsReadOnlyFalse()
		{
			var view = new Text("Hello").IsReadOnly(false);

			var isReadOnly = view.GetEnvironment<bool>("View.IsReadOnly");
			Assert.False(isReadOnly);
		}

		[Fact]
		public void SemanticDescriptionOverwritesPrevious()
		{
			var view = new Text("Hello")
				.SemanticDescription("First")
				.SemanticDescription("Second");

			var semantics = ((Microsoft.Maui.IView)view).Semantics;
			Assert.Equal("Second", semantics.Description);
		}

		[Fact]
		public void DifferentHeadingLevels()
		{
			foreach (var level in new[] { Microsoft.Maui.SemanticHeadingLevel.None, Microsoft.Maui.SemanticHeadingLevel.Level1, Microsoft.Maui.SemanticHeadingLevel.Level2, Microsoft.Maui.SemanticHeadingLevel.Level3 })
			{
				var view = new Text("Test").SemanticHeadingLevel(level);
				var semantics = ((Microsoft.Maui.IView)view).Semantics;
				Assert.Equal(level, semantics.HeadingLevel);
			}
		}
	}
}
