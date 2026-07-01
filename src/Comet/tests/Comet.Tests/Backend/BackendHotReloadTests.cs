#nullable enable
using Comet;
using Comet.Backend;
using Comet.HotReload;
using Comet.Reactive;
using Microsoft.Maui.HotReload;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks the hot-reload / Reload() path on the node backend: a reload transfers the
	/// retained <see cref="ICometBackendNode"/> from the old view tree to the rebuilt one
	/// (no re-materialization), re-emits changed properties as patches, rebinds events to
	/// the new views, and preserves component state across a hot-reload type replacement.
	/// The legacy-path equivalents live in HotReloadTests/ComponentHotReloadTests.
	/// </summary>
	public class BackendHotReloadTests
	{
		static BackendHotReloadTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new NullServiceProvider());

		sealed class NullServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		class CounterState
		{
			public int Count { get; set; }
		}

		class CounterComponent : Component<CounterState>
		{
			public override View Render() => new Text($"Count: {State.Count}");
			public void SetCount(int value) => SetState(state => state.Count = value);
		}

		class CounterReplacementComponent : Component<CounterState>
		{
			public override View Render() => new Text($"Updated: {State.Count}");
		}

		class LabelHostView : View
		{
			public string Label = "before";

			[Body]
			View body() => new VStack { new Text(Label) };
		}

		class ClickHostView : View
		{
			public int Clicks;
			public string ButtonTitle = "Tap";

			[Body]
			View body() => new VStack { new Button(ButtonTitle, () => Clicks++) };
		}

		class CounterHostView : View
		{
			public readonly CounterComponent Counter = new();

			[Body]
			View body() => new VStack { Counter };
		}

		class RootV1 : View
		{
			[Body]
			View body() => new VStack { new Text("v1") };
		}

		class RootV2 : View
		{
			[Body]
			View body() => new VStack { new Text("v2") };
		}

		[Fact]
		public void Reload_PatchesExistingNode_WithoutRematerializing()
		{
			MauiHotReloadHelper.IsEnabled = true;
			var host = new LabelHostView();
			var stackNode = Bridge(host);
			var textNode = stackNode.Children[0];
			Assert.Equal("before", textNode.Get(PropertyIds.Text_Value).AsString);

			host.Label = "after";
			host.Reload();

			// The SAME retained node instances got the patch — no re-materialization.
			Assert.Same(textNode, stackNode.Children[0]);
			Assert.Equal("after", textNode.Get(PropertyIds.Text_Value).AsString);
			Assert.False(textNode.Disposed);
		}

		[Fact]
		public void Reload_RebindsEventsToTheNewViews()
		{
			MauiHotReloadHelper.IsEnabled = true;
			var host = new ClickHostView();
			var stackNode = Bridge(host);
			var buttonNode = stackNode.Children[0];

			buttonNode.Sink!.OnEvent(EventIds.Clicked);
			Assert.Equal(1, host.Clicks);

			host.ButtonTitle = "Tap v2";
			host.Reload();

			Assert.Equal("Tap v2", buttonNode.Get(PropertyIds.Button_Text).AsString);
			buttonNode.Sink!.OnEvent(EventIds.Clicked);
			Assert.Equal(2, host.Clicks);
		}

		[Fact]
		public void HotReload_ReplacesRootViewType_ViaTriggerReload()
		{
			// The device shape: the app root itself is the replaced type (the
			// CometComposeProbe HotReloadDemo scenario).
			MauiHotReloadHelper.IsEnabled = true;
			var root = new RootV1();
			var stackNode = Bridge(root);
			var textNode = stackNode.Children[0];
			Assert.Equal("v1", textNode.Get(PropertyIds.Text_Value).AsString);

			CometHotReloadHelper.RegisterReplacedView(typeof(RootV1).FullName!, typeof(RootV2));
			MauiHotReloadHelper.TriggerReload();

			Assert.Same(textNode, stackNode.Children[0]);
			Assert.Equal("v2", textNode.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void HotReload_ReplacesComponent_PreservingStateAndPatchingNode()
		{
			// MauiHotReloadHelper.TriggerReload only reloads ROOT views (Parent == null),
			// so the app shape matters: a [Body] host re-renders and its diff reconciles
			// the nested component — the same contract the legacy handler path had.
			MauiHotReloadHelper.IsEnabled = true;
			var host = new CounterHostView();
			var stackNode = Bridge(host);
			var textNode = stackNode.Children[0];
			Assert.Equal("Count: 0", textNode.Get(PropertyIds.Text_Value).AsString);

			host.Counter.SetCount(7);
			ReactiveScheduler.FlushSync();
			Assert.Equal("Count: 7", textNode.Get(PropertyIds.Text_Value).AsString);

			CometHotReloadHelper.RegisterReplacedView(
				typeof(CounterComponent).FullName!, typeof(CounterReplacementComponent));
			MauiHotReloadHelper.TriggerReload();

			// Replacement code runs, state carried over, and the patch landed on the
			// SAME retained node (the transfer path, not a rebuild).
			Assert.Same(textNode, stackNode.Children[0]);
			Assert.Equal("Updated: 7", textNode.Get(PropertyIds.Text_Value).AsString);
		}
	}
}
