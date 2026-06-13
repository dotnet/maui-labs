#nullable enable
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks the two-way input path for the Compose backend (TextField, Toggle): the
	/// control emits its value to the node, and a user edit routed back through the event
	/// sink writes through to the bound Signal and re-renders dependents.
	/// </summary>
	public class BackendInputTests
	{
		static BackendInputTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void TextField_EmitsTextAndPlaceholder()
		{
			var node = Bridge(new VStack { new TextField("hi", "type here") });
			var field = node.Children[0];
			Assert.Equal("hi", field.Get(PropertyIds.TextField_Text).AsString);
			Assert.Equal("type here", field.Get(PropertyIds.TextField_Placeholder).AsString);
		}

		[Fact]
		public void TextField_Edit_WritesBackToSignal()
		{
			var name = new Signal<string>("");
			var node = Bridge(new VStack { new TextField(name) });
			var field = node.Children[0];

			Assert.NotNull(field.Sink);
			field.Sink!.OnEvent(EventIds.TextChanged, "Dave");

			Assert.Equal("Dave", name.Value);
		}

		[Fact]
		public void TextField_Edit_RerendersDependentText()
		{
			var name = new Signal<string>("");
			var root = new VStack
			{
				new TextField(name),
				new Text(() => $"Hi {name.Value}"),
			};
			var node = Bridge(root);
			var field = node.Children[0];
			var greeting = node.Children[1];

			field.Sink!.OnEvent(EventIds.TextChanged, "Dave");
			ReactiveScheduler.FlushSync();

			Assert.Equal("Hi Dave", greeting.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void Toggle_EmitsIsOnAndWritesBack()
		{
			var on = new Signal<bool>(false);
			var node = Bridge(new VStack { new Toggle(on) });
			var toggle = node.Children[0];

			Assert.False(toggle.Get(PropertyIds.Toggle_IsOn).AsBool);

			toggle.Sink!.OnEvent(EventIds.Toggled, true);
			Assert.True(on.Value);
		}

		[Fact]
		public void Toggle_OwnVisualReflectsValueAfterWriteBack()
		{
			// The controlled-component round-trip: after the user flips the switch and the
			// value writes back to the signal, the toggle's OWN node must reflect the new
			// value (so the Compose Switch knob moves), not just dependent views.
			var on = new Signal<bool>(false);
			var node = Bridge(new VStack { new Toggle(on) });
			var toggle = node.Children[0];

			toggle.Sink!.OnEvent(EventIds.Toggled, true);
			ReactiveScheduler.FlushSync();

			Assert.True(toggle.Get(PropertyIds.Toggle_IsOn).AsBool);
		}

		[Fact]
		public void Toggle_DrivesDependentVisibilityText()
		{
			var on = new Signal<bool>(false);
			var root = new VStack
			{
				new Toggle(on),
				new Text(() => on.Value ? "ON" : "OFF"),
			};
			var node = Bridge(root);
			var toggle = node.Children[0];
			var label = node.Children[1];
			Assert.Equal("OFF", label.Get(PropertyIds.Text_Value).AsString);

			toggle.Sink!.OnEvent(EventIds.Toggled, true);
			ReactiveScheduler.FlushSync();

			Assert.Equal("ON", label.Get(PropertyIds.Text_Value).AsString);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
