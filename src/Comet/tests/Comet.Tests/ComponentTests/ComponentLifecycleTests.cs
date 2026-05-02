using System;
using System.Collections.Generic;
using Comet.Tests.Handlers;
using Xunit;

namespace Comet.Tests
{
	public class ComponentLifecycleTests : TestBase
	{
		// Tracks lifecycle event order
		class LifecycleComponent : Component
		{
			public List<string> Events { get; } = new List<string>();

			public LifecycleComponent()
			{
				Events.Add("constructed");
			}

			public override View Render()
			{
				Events.Add("render");
				return new Text("Lifecycle test");
			}

			protected override void OnMounted()
			{
				Events.Add("mounted");
			}

			protected override void OnWillUnmount()
			{
				Events.Add("will_unmount");
			}
		}

		// Stateful lifecycle component to verify state availability
		class StatefulState
		{
			public int Value { get; set; } = 42;
		}

		class StatefulLifecycleComponent : Component<StatefulState>
		{
			public bool StateWasAvailableDuringRender { get; private set; }
			public int StateValueDuringRender { get; private set; }
			public List<string> Events { get; } = new List<string>();

			public override View Render()
			{
				Events.Add("render");
				StateWasAvailableDuringRender = State != null;
				if (State != null)
					StateValueDuringRender = State.Value;
				return new Text($"Value: {State?.Value}");
			}

			protected override void OnMounted()
			{
				Events.Add("mounted");
			}

			protected override void OnWillUnmount()
			{
				Events.Add("will_unmount");
			}
		}

		[Fact]
		public void OnMountedCalledOnFirstRender()
		{
			var component = new LifecycleComponent();
			component.SetViewHandlerToGeneric();

			Assert.Contains("mounted", component.Events);
		}

		[Fact]
		public void OnWillUnmountCalledOnDisposal()
		{
			var component = new LifecycleComponent();
			component.SetViewHandlerToGeneric();

			component.Dispose();

			Assert.Contains("will_unmount", component.Events);
		}

		[Fact]
		public void LifecycleOrderIsCorrect()
		{
			var component = new LifecycleComponent();
			component.SetViewHandlerToGeneric();

			// Expected order: construction → render → mounted
			Assert.True(component.Events.Count >= 3,
				$"Expected at least 3 lifecycle events, got {component.Events.Count}: [{string.Join(", ", component.Events)}]");

			var constructedIdx = component.Events.IndexOf("constructed");
			var renderIdx = component.Events.IndexOf("render");
			var mountedIdx = component.Events.IndexOf("mounted");

			Assert.True(constructedIdx >= 0, "constructed event should fire");
			Assert.True(renderIdx >= 0, "render event should fire");
			Assert.True(mountedIdx >= 0, "mounted event should fire");

			Assert.True(constructedIdx < renderIdx,
				"constructed should come before render");
			Assert.True(renderIdx < mountedIdx,
				"render should come before mounted");
		}

		[Fact]
		public void StateIsAvailableDuringRender()
		{
			var component = new StatefulLifecycleComponent();
			component.SetViewHandlerToGeneric();

			Assert.True(component.StateWasAvailableDuringRender,
				"State should be initialized before Render() is called");
			Assert.Equal(42, component.StateValueDuringRender);
		}

		[Fact]
		public void FullLifecycleWithDisposal()
		{
			var component = new LifecycleComponent();
			component.SetViewHandlerToGeneric();
			component.Dispose();

			// Verify full lifecycle played out
			Assert.Contains("constructed", component.Events);
			Assert.Contains("render", component.Events);
			Assert.Contains("mounted", component.Events);
			Assert.Contains("will_unmount", component.Events);
		}

		[Fact]
		public void StatefulComponentLifecycleOrder()
		{
			var component = new StatefulLifecycleComponent();
			component.SetViewHandlerToGeneric();

			var renderIdx = component.Events.IndexOf("render");
			var mountedIdx = component.Events.IndexOf("mounted");

			Assert.True(renderIdx >= 0, "render should fire");
			Assert.True(mountedIdx >= 0, "mounted should fire");
			Assert.True(renderIdx < mountedIdx,
				"render should come before mounted for stateful components too");
		}

		[Fact]
		public void DisposingStatefulComponentCallsOnWillUnmount()
		{
			var component = new StatefulLifecycleComponent();
			component.SetViewHandlerToGeneric();

			component.Dispose();

			Assert.Contains("will_unmount", component.Events);
		}
	}
}
