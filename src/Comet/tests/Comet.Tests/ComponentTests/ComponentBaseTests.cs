using System;
using Comet.Tests.Handlers;
using Xunit;

namespace Comet.Tests
{
	public class ComponentBaseTests : TestBase
	{
		// Minimal concrete component for testing
		class HelloComponent : Component
		{
			public int RenderCallCount { get; private set; }

			public override View Render()
			{
				RenderCallCount++;
				return new Text("Hello from Component");
			}
		}

		// Component that returns a layout tree
		class LayoutComponent : Component
		{
			public override View Render()
			{
				return new VStack
				{
					new Text("Title"),
					new Text("Subtitle"),
				};
			}
		}

		// A parent view that embeds a component
		class ParentView : View
		{
			public readonly HelloComponent child = new HelloComponent();

			[Body]
			View body() => new VStack
			{
				new Text("Parent"),
				child,
			};
		}

		[Fact]
		public void ConcreteComponentCanBeCreated()
		{
			var component = new HelloComponent();
			Assert.NotNull(component);
			Assert.IsAssignableFrom<View>(component);
		}

		[Fact]
		public void RenderIsCalledWhenComponentIsBuilt()
		{
			var component = new HelloComponent();
			component.SetViewHandlerToGeneric();

			Assert.True(component.RenderCallCount > 0, "Render() should be called when the component is built");
		}

		[Fact]
		public void RenderOutputIsUsedAsBody()
		{
			var component = new HelloComponent();
			component.SetViewHandlerToGeneric();

			var builtView = component.BuiltView;
			Assert.NotNull(builtView);
			Assert.IsType<Text>(builtView);
		}

		[Fact]
		public void RenderCanReturnLayoutTree()
		{
			var component = new LayoutComponent();
			component.SetViewHandlerToGeneric();

			var builtView = component.BuiltView;
			Assert.NotNull(builtView);
			Assert.IsType<VStack>(builtView);
		}

		[Fact]
		public void ComponentCanBeUsedInsideAnotherView()
		{
			var parent = new ParentView();
			parent.SetViewHandlerToGeneric();

			var builtView = parent.BuiltView;
			Assert.NotNull(builtView);
			Assert.IsType<VStack>(builtView);
		}

		[Fact]
		public void ComponentIsAView()
		{
			var component = new HelloComponent();
			View view = component;
			Assert.NotNull(view);
		}
	}
}
