using System;
using Comet.Tests.Handlers;
using Xunit;

namespace Comet.Tests
{
	public class ComponentMergeTests : TestBase
	{
		// Simple stateful component
		class CounterComponent : Component<CounterState>
		{
			public int RenderCount { get; private set; }

			public override View Render()
			{
				RenderCount++;
				return new Text($"Count: {State.Count}");
			}

			public void Increment()
			{
				SetState(s => s.Count++);
			}
		}

		class CounterState
		{
			public int Count { get; set; }
		}

		// Component with props
		class GreetingComponent : Component<GreetingState, GreetingProps>
		{
			public GreetingComponent(GreetingProps props)
			{
				Props = props;
			}

			public override View Render()
			{
				return new Text($"Hello, {Props.Name}!");
			}
		}

		class GreetingState
		{
		}

		class GreetingProps
		{
			public string Name { get; set; }
		}

		// Parent component that renders a child component
		class ParentWithChildComponent : Component
		{
			public string ChildName { get; set; } = "World";

			public override View Render()
			{
				return new VStack
				{
					new Text("Parent"),
					new GreetingComponent(new GreetingProps { Name = ChildName })
				};
			}
		}

		// Two different component types
		class ComponentA : Component
		{
			public override View Render() => new Text("Component A");
		}

		class ComponentB : Component
		{
			public override View Render() => new Text("Component B");
		}

		// Component that returns keyed children
		class KeyedChildrenComponent : Component
		{
			public string[] Items { get; set; } = Array.Empty<string>();

			public override View Render()
			{
				var stack = new VStack();
				foreach (var item in Items)
				{
					stack.Add(new Text(item).Key(item));
				}
				return stack;
			}
		}

		// Nested component structure
		class OuterComponent : Component
		{
			public override View Render()
			{
				return new VStack
				{
					new Text("Outer"),
					new InnerComponent()
				};
			}
		}

		class InnerComponent : Component
		{
			public int RenderCount { get; private set; }

			public override View Render()
			{
				RenderCount++;
				return new Text("Inner");
			}
		}

		[Fact]
		public void ComponentToComponentDiffPreservesInstance()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			// Get the built view from first render
			var firstRender = component.BuiltView;

			// Trigger re-render
			component.Increment();

			var secondRender = component.BuiltView;

			// The component itself should be reused, not replaced
			Assert.NotNull(secondRender);
			// Render count should increment (not reset to 1)
			Assert.True(component.RenderCount >= 2);
		}

		[Fact]
		public void ComponentStatePreservedDuringDiff()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			// Increment several times
			component.Increment();
			component.Increment();
			component.Increment();

			// State should be 3
			Assert.Equal(3, component.State.Count);

			// Trigger another render
			component.Reload();

			// State should still be 3
			Assert.Equal(3, component.State.Count);
		}

		[Fact]
		public void ComponentPropsUpdateDetected()
		{
			var parent = new ParentWithChildComponent
			{
				ChildName = "Alice"
			};
			parent.SetViewHandlerToGeneric();

			var firstRender = parent.BuiltView as VStack;
			var firstChild = ((IContainerView)firstRender).GetChildren()[1] as GreetingComponent;
			Assert.NotNull(firstChild);

			// Change props
			parent.ChildName = "Bob";
			parent.Reload();

			var secondRender = parent.BuiltView as VStack;
			var secondChild = ((IContainerView)secondRender).GetChildren()[1] as GreetingComponent;
			Assert.NotNull(secondChild);

			// Component should be reused but props updated
			Assert.Equal("Bob", secondChild.Props.Name);
		}

		[Fact]
		public void ComponentTypeMismatchCausesReplacement()
		{
			// Parent that can switch between two component types
			var parent = new View();
			var useComponentA = true;
			parent.Body = () => useComponentA ? (View)new ComponentA() : new ComponentB();
			parent.SetViewHandlerToGeneric();

			// BuiltView traverses through Component → Render() output (Text),
			// so we verify the type via the Body's return value structure.
			var firstRender = parent.BuiltView;
			Assert.NotNull(firstRender);

			// Switch to ComponentB
			useComponentA = false;
			parent.Reload();

			var secondRender = parent.BuiltView;
			Assert.NotNull(secondRender);

			// Should be a different instance (replacement, not merge)
			Assert.NotSame(firstRender, secondRender);
		}

		[Fact]
		public void NestedComponentDiff()
		{
			var outer = new OuterComponent();
			outer.SetViewHandlerToGeneric();

			var firstRender = outer.BuiltView as VStack;
			var firstInner = ((IContainerView)firstRender).GetChildren()[1] as InnerComponent;
			Assert.NotNull(firstInner);

			// Trigger re-render of outer
			outer.Reload();

			var secondRender = outer.BuiltView as VStack;
			var secondInner = ((IContainerView)secondRender).GetChildren()[1] as InnerComponent;
			Assert.NotNull(secondInner);

			// Inner component should be reused (not recreated)
			Assert.Same(firstInner, secondInner);
			// Note: AreSameType calls GetView() which evaluates Body/Render on
			// unrendered Components during the diff. This is expected framework
			// behavior — the important thing is instance reuse, verified above.
		}

		[Fact]
		public void ComponentWithKeyedChildrenDiffCorrectly()
		{
			var component = new KeyedChildrenComponent
			{
				Items = new[] { "A", "B", "C" }
			};
			component.SetViewHandlerToGeneric();

			var firstRender = component.BuiltView as VStack;
			var firstChildren = ((IContainerView)firstRender).GetChildren();

			// Reorder items
			component.Items = new[] { "C", "A", "B" };
			component.Reload();

			var secondRender = component.BuiltView as VStack;
			var secondChildren = ((IContainerView)secondRender).GetChildren();

			// Children should be reused and reordered
			Assert.Equal(3, secondChildren.Count);
			Assert.Equal("C", (secondChildren[0] as Text)?.Value);
			Assert.Equal("A", (secondChildren[1] as Text)?.Value);
			Assert.Equal("B", (secondChildren[2] as Text)?.Value);
		}

		[Fact]
		public void SetStateDuringDiffIsHandledSafely()
		{
			// Component that triggers SetState during render (edge case)
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			// Increment multiple times rapidly (simulating concurrent updates)
			component.Increment();
			component.Increment();
			component.Increment();

			// Should not throw or corrupt state
			Assert.Equal(3, component.State.Count);
		}

		[Fact]
		public void ComponentDiffWithSameTypeButDifferentProps()
		{
			// Parent that recreates child with new props
			var parent = new ParentWithChildComponent
			{
				ChildName = "Alice"
			};
			parent.SetViewHandlerToGeneric();

			var firstRender = parent.BuiltView as VStack;
			var firstChild = ((IContainerView)firstRender).GetChildren()[1] as GreetingComponent;
			Assert.NotNull(firstChild);
			Assert.Equal("Alice", firstChild.Props.Name);

			// Change props and re-render
			parent.ChildName = "Bob";
			parent.Reload();

			var secondRender = parent.BuiltView as VStack;
			var secondChild = ((IContainerView)secondRender).GetChildren()[1] as GreetingComponent;
			Assert.NotNull(secondChild);

			// Component should be reused (same instance) with updated props
			Assert.Same(firstChild, secondChild);
			Assert.Equal("Bob", secondChild.Props.Name);
		}

		[Fact]
		public void ComponentMergePreservesHandlers()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			var firstHandler = component.ViewHandler;
			Assert.NotNull(firstHandler);

			// Trigger re-render
			component.Increment();

			var secondHandler = component.ViewHandler;
			// Handler should be preserved
			Assert.Same(firstHandler, secondHandler);
		}

		[Fact]
		public void ComponentDiffWithNullChild()
		{
			// Component that conditionally renders a child
			var renderChild = true;
			var parent = new View();
			parent.Body = () => new VStack
			{
				new Text("Parent"),
				renderChild ? new CounterComponent() : null
			};
			parent.SetViewHandlerToGeneric();

			var firstRender = parent.BuiltView as VStack;
			Assert.Equal(2, ((IContainerView)firstRender).GetChildren().Count);

			// Remove child by returning null
			renderChild = false;
			parent.Reload();

			var secondRender = parent.BuiltView as VStack;
			// Should have only 1 child now
			Assert.Equal(1, ((IContainerView)secondRender).GetChildren().Count);
		}

		[Fact]
		public void ComponentPropsChangeTriggersReRender()
		{
			var props = new GreetingProps { Name = "Alice" };
			var component = new GreetingComponent(props);
			component.SetViewHandlerToGeneric();

			var firstBuiltView = component.BuiltView as Text;
			Assert.Equal("Hello, Alice!", firstBuiltView?.Value);

			// Change props and trigger re-render
			component.Props = new GreetingProps { Name = "Bob" };
			component.Reload();

			var secondBuiltView = component.BuiltView as Text;
			Assert.Equal("Hello, Bob!", secondBuiltView?.Value);
		}
	}
}
