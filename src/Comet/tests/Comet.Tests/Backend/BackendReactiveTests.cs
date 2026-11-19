#nullable enable
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks the reactive-update and event-routing paths that drive the Compose backend
	/// (verified on-device, asserted here host-side via FakeBackendNode): a Signal change
	/// re-emits the bound property to the node, and a node event routes back to the
	/// control's handler.
	/// </summary>
	public class BackendReactiveTests
	{
		static BackendReactiveTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void SignalChange_ReEmitsBoundPropertyToNode()
		{
			var count = new Signal<int>(0);
			var root = new VStack { new Text(() => $"Count: {count.Value}") };

			var node = Bridge(root);
			var textNode = node.Children[0];
			Assert.Equal("Count: 0", textNode.Get(PropertyIds.Text_Value).AsString);

			count.Value = 5;
			ReactiveScheduler.FlushSync();

			Assert.Equal("Count: 5", textNode.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void DerivedValue_TracksMultipleSignals()
		{
			var a = new Signal<int>(1);
			var b = new Signal<int>(2);
			var root = new VStack { new Text(() => $"Sum: {a.Value + b.Value}") };

			var node = Bridge(root);
			var sumNode = node.Children[0];
			Assert.Equal("Sum: 3", sumNode.Get(PropertyIds.Text_Value).AsString);

			a.Value = 10;
			ReactiveScheduler.FlushSync();
			Assert.Equal("Sum: 12", sumNode.Get(PropertyIds.Text_Value).AsString);

			b.Value = 100;
			ReactiveScheduler.FlushSync();
			Assert.Equal("Sum: 110", sumNode.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void NodeEvent_RoutesToButtonClickedHandler()
		{
			var clicks = 0;
			var root = new VStack { new Button("Tap", () => clicks++) };

			var node = Bridge(root);
			var buttonNode = node.Children[0];

			Assert.NotNull(buttonNode.Sink);
			buttonNode.Sink!.OnEvent(EventIds.Clicked);
			buttonNode.Sink!.OnEvent(EventIds.Clicked);

			Assert.Equal(2, clicks);
		}

		[Fact]
		public void ButtonClick_DrivesSignalThatRecomposesText()
		{
			// The full on-device loop, host-side: click -> Clicked -> Signal++ -> flush ->
			// bound Text re-emits to its node.
			var count = new Signal<int>(0);
			var root = new VStack
			{
				new Text(() => $"Count: {count.Value}"),
				new Button("Increment", () => count.Value++),
			};

			var node = Bridge(root);
			var textNode = node.Children[0];
			var buttonNode = node.Children[1];

			buttonNode.Sink!.OnEvent(EventIds.Clicked);
			ReactiveScheduler.FlushSync();
			Assert.Equal("Count: 1", textNode.Get(PropertyIds.Text_Value).AsString);

			buttonNode.Sink!.OnEvent(EventIds.Clicked);
			buttonNode.Sink!.OnEvent(EventIds.Clicked);
			ReactiveScheduler.FlushSync();
			Assert.Equal("Count: 3", textNode.Get(PropertyIds.Text_Value).AsString);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
