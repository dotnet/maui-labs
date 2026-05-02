using System;
using Comet.Tests.Handlers;
using Xunit;

namespace Comet.Tests
{
	public class ComponentStateTests : TestBase
	{
		// Simple state class for counter tests
		class CounterState
		{
			public int Count { get; set; }
		}

		// State class with multiple properties
		class ProfileState
		{
			public string Name { get; set; } = "";
			public int Age { get; set; }
			public bool IsActive { get; set; }
		}

		// Minimal stateful component
		class CounterComponent : Component<CounterState>
		{
			public int RenderCallCount { get; private set; }

			public override View Render()
			{
				RenderCallCount++;
				return new Text($"Count: {State.Count}");
			}

			public void Increment() => SetState(s => s.Count++);

			public void SetCount(int value) => SetState(s => s.Count = value);
		}

		// Component with multi-property state
		class ProfileComponent : Component<ProfileState>
		{
			public int RenderCallCount { get; private set; }

			public override View Render()
			{
				RenderCallCount++;
				return new VStack
				{
					new Text($"Name: {State.Name}"),
					new Text($"Age: {State.Age}"),
					new Text($"Active: {State.IsActive}"),
				};
			}

			public void UpdateName(string name) => SetState(s => s.Name = name);
			public void UpdateAge(int age) => SetState(s => s.Age = age);
			public void UpdateAll(string name, int age, bool active) => SetState(s =>
			{
				s.Name = name;
				s.Age = age;
				s.IsActive = active;
			});
		}

		[Fact]
		public void StateIsInitializedToNewInstance()
		{
			var component = new CounterComponent();
			Assert.NotNull(component.State);
			Assert.Equal(0, component.State.Count);
		}

		[Fact]
		public void SetStateMutatesState()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			component.Increment();

			Assert.Equal(1, component.State.Count);
		}

		[Fact]
		public void SetStateTriggersReRender()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			var initialRenderCount = component.RenderCallCount;

			component.Increment();

			Assert.True(component.RenderCallCount > initialRenderCount,
				"SetState should trigger a re-render");
		}

		[Fact]
		public void MultipleSequentialSetStateCalls()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			component.Increment();
			component.Increment();
			component.Increment();

			Assert.Equal(3, component.State.Count);
		}

		[Fact]
		public void SetStateWithAbsoluteValue()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			component.SetCount(42);

			Assert.Equal(42, component.State.Count);
		}

		[Fact]
		public void MultiPropertyStateMutation()
		{
			var component = new ProfileComponent();
			component.SetViewHandlerToGeneric();

			component.UpdateName("Alice");
			Assert.Equal("Alice", component.State.Name);

			component.UpdateAge(30);
			Assert.Equal(30, component.State.Age);
		}

		[Fact]
		public void SetStateMutatesMultiplePropertiesAtOnce()
		{
			var component = new ProfileComponent();
			component.SetViewHandlerToGeneric();

			component.UpdateAll("Bob", 25, true);

			Assert.Equal("Bob", component.State.Name);
			Assert.Equal(25, component.State.Age);
			Assert.True(component.State.IsActive);
		}

		[Fact]
		public void EachSetStateCallTriggersReRender()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			var rendersBefore = component.RenderCallCount;

			component.Increment();
			var rendersAfterFirst = component.RenderCallCount;

			component.Increment();
			var rendersAfterSecond = component.RenderCallCount;

			Assert.True(rendersAfterFirst > rendersBefore);
			Assert.True(rendersAfterSecond > rendersAfterFirst);
		}

		[Fact]
		public void ReactiveWorksAsStateReplacement()
		{
			// Reactive<T> should be usable anywhere Reactive<T> is
			var reactive = new Reactive<int>();
			reactive.Value = 42;
			Assert.Equal(42, reactive.Value);

			// Verify it's a Reactive<T>
			Assert.IsAssignableFrom<Reactive<int>>(reactive);
		}

		[Fact]
		public void ReactiveNotifiesOnValueChange()
		{
			var reactive = new Reactive<string>();
			int notifyCount = 0;

			reactive.ValueChanged = (val) => notifyCount++;
			reactive.Value = "hello";

			Assert.Equal("hello", reactive.Value);
			Assert.True(notifyCount > 0);
		}

		[Fact]
		public void ReactiveImplicitConversion()
		{
			Reactive<int> reactive = new Reactive<int>();
			reactive.Value = 99;

			// Should be assignable to Reactive<T> reference
			Reactive<int> state = reactive;
			Assert.Equal(99, state.Value);
		}
	}
}
