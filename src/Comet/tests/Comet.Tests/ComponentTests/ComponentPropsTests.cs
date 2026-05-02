using System;
using System.Collections.Generic;
using Comet.Tests.Handlers;
using Xunit;

namespace Comet.Tests
{
	public class ComponentPropsTests : TestBase
	{
		class EmptyState
		{
		}

		// Simple props with primitive values
		class GreetingProps
		{
			public string Name { get; set; } = "";
			public int FontSize { get; set; } = 14;
		}

		// Complex props with nested types
		class DashboardProps
		{
			public string Title { get; set; } = "";
			public List<string> Items { get; set; } = new List<string>();
			public Action OnRefresh { get; set; }
		}

		// Component with state and props
		class GreetingComponent : Component<EmptyState, GreetingProps>
		{
			public int RenderCallCount { get; private set; }

			public override View Render()
			{
				RenderCallCount++;
				return new Text($"Hello, {Props.Name}!");
			}
		}

		// Component with complex props
		class DashboardComponent : Component<EmptyState, DashboardProps>
		{
			public int RenderCallCount { get; private set; }

			public override View Render()
			{
				RenderCallCount++;
				return new VStack
				{
					new Text(Props.Title),
					new Text($"Items: {Props.Items.Count}"),
				};
			}
		}

		// Stateful component with props
		class CounterState
		{
			public int InternalCount { get; set; }
		}

		class CounterProps
		{
			public int InitialValue { get; set; }
			public string Label { get; set; } = "Count";
		}

		class CounterWithPropsComponent : Component<CounterState, CounterProps>
		{
			public int RenderCallCount { get; private set; }

			public override View Render()
			{
				RenderCallCount++;
				return new Text($"{Props.Label}: {State.InternalCount}");
			}

			public void Increment() => SetState(s => s.InternalCount++);
		}

		[Fact]
		public void PropsInitializedToNewInstance()
		{
			var component = new GreetingComponent();
			Assert.NotNull(component.Props);
		}

		[Fact]
		public void PropsDefaultValuesAreSet()
		{
			var component = new GreetingComponent();
			Assert.Equal("", component.Props.Name);
			Assert.Equal(14, component.Props.FontSize);
		}

		[Fact]
		public void SettingPropsFromParent()
		{
			var component = new GreetingComponent();
			component.Props = new GreetingProps { Name = "World", FontSize = 20 };

			Assert.Equal("World", component.Props.Name);
			Assert.Equal(20, component.Props.FontSize);
		}

		[Fact]
		public void ChangingPropsTriggersReRender()
		{
			var component = new GreetingComponent();
			component.SetViewHandlerToGeneric();

			var initialRenderCount = component.RenderCallCount;

			component.Props = new GreetingProps { Name = "Updated" };

			Assert.True(component.RenderCallCount > initialRenderCount,
				"Changing Props should trigger a re-render");
		}

		[Fact]
		public void PropsWithComplexTypes()
		{
			var component = new DashboardComponent();
			component.Props = new DashboardProps
			{
				Title = "My Dashboard",
				Items = new List<string> { "Item 1", "Item 2", "Item 3" },
			};

			component.SetViewHandlerToGeneric();

			Assert.Equal("My Dashboard", component.Props.Title);
			Assert.Equal(3, component.Props.Items.Count);
		}

		[Fact]
		public void PropsWithActionCallback()
		{
			bool refreshCalled = false;
			var component = new DashboardComponent();
			component.Props = new DashboardProps
			{
				Title = "Test",
				OnRefresh = () => refreshCalled = true,
			};

			component.Props.OnRefresh?.Invoke();
			Assert.True(refreshCalled);
		}

		[Fact]
		public void StateAndPropsCoexist()
		{
			var component = new CounterWithPropsComponent();
			component.Props = new CounterProps { Label = "Score", InitialValue = 10 };
			component.SetViewHandlerToGeneric();

			Assert.NotNull(component.State);
			Assert.NotNull(component.Props);
			Assert.Equal("Score", component.Props.Label);

			component.Increment();
			Assert.Equal(1, component.State.InternalCount);
		}

		[Fact]
		public void PropsCanBeReplacedMultipleTimes()
		{
			var component = new GreetingComponent();
			component.SetViewHandlerToGeneric();

			component.Props = new GreetingProps { Name = "First" };
			Assert.Equal("First", component.Props.Name);

			component.Props = new GreetingProps { Name = "Second" };
			Assert.Equal("Second", component.Props.Name);

			component.Props = new GreetingProps { Name = "Third" };
			Assert.Equal("Third", component.Props.Name);
		}
	}
}
