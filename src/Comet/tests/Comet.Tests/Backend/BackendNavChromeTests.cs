#nullable enable
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks the nav chrome contract (Reply's adaptive NavigationSuite programs against it):
	/// NavigationBar/NavigationRail push the selected index to their node and patch it on
	/// signal change; SelectItem is the single selection entry point (signal write + OnSelect);
	/// items expose their icon/label views as bridgeable children.
	/// </summary>
	public class BackendNavChromeTests
	{
		static BackendNavChromeTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		static NavigationItem Item(string label, System.Action? onSelect = null)
			=> new(new Icon("circle"), new Text(label), onSelect);

		[Fact]
		public void NavigationBar_PushesSelectedIndex_AndPatchesOnSignalChange()
		{
			var selected = new Signal<int>(1);
			var bar = new NavigationBar(selected, new[] { Item("a"), Item("b"), Item("c") });

			var node = Bridge(bar);
			Assert.Equal(1, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);

			selected.Value = 2;
			Assert.Equal(2, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);
		}

		[Fact]
		public void NavigationBar_SelectItem_WritesSignal_AndInvokesOnSelect()
		{
			int invoked = -1;
			var selected = new Signal<int>(0);
			var bar = new NavigationBar(selected, new[]
			{
				Item("a", () => invoked = 0),
				Item("b", () => invoked = 1),
			});
			var node = Bridge(bar);

			bar.SelectItem(1);
			Assert.Equal(1, selected.Value);
			Assert.Equal(1, invoked);
			Assert.Equal(1, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);

			bar.SelectItem(5);   // out of range: signal still moves, no crash, no callback
			Assert.Equal(5, selected.Value);
			Assert.Equal(1, invoked);
		}

		[Fact]
		public void NavigationBar_SelectedIndex_DrivesReactiveChrome()
		{
			// The adaptive-suite pattern: sibling content switches on the same signal the
			// bar writes — one tap re-renders exactly the views that read it.
			var selected = new Signal<int>(0);
			var bar = new NavigationBar(selected, new[] { Item("inbox"), Item("articles") });
			var root = new VStack
			{
				new Text(() => selected.Value == 0 ? "inbox" : "articles"),
				bar,
			};

			var node = Bridge(root);
			var text = node.Children[0];
			Assert.Equal("inbox", text.Get(PropertyIds.Text_Value).AsString);

			bar.SelectItem(1);
			ReactiveScheduler.FlushSync();
			Assert.Equal("articles", text.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void NavigationBar_BridgesItemIconAndLabelAsChildren()
		{
			var bar = new NavigationBar(new Signal<int>(0), new[] { Item("a"), Item("b") });
			var node = Bridge(bar);

			// Host bridge (no own-content node) walks the full structure:
			// bar → 2 items → icon + label each.
			Assert.Equal(2, node.Children.Count);
			Assert.All(node.Children, item => Assert.Equal(2, item.Children.Count));
		}

		[Fact]
		public void NavigationItem_WithoutLabel_ExposesOnlyIcon()
		{
			var item = new NavigationItem(new Icon("circle"));
			Assert.Single(item.GetChildren());
		}

		[Fact]
		public void NavigationRail_SameContract_WithHeaderFirst()
		{
			int invoked = -1;
			var selected = new Signal<int>(0);
			var header = new VStack { new Icon("menu") };
			var rail = new NavigationRail(selected,
				new[] { Item("a"), Item("b", () => invoked = 1) }, header);

			var node = Bridge(rail);
			Assert.Equal(0, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);
			Assert.Equal(3, node.Children.Count);            // header + 2 items
			Assert.Equal("VStack", node.Children[0].Kind);   // header bridges first

			rail.SelectItem(1);
			Assert.Equal(1, selected.Value);
			Assert.Equal(1, invoked);
			Assert.Equal(1, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);
		}

		[Theory]
		[InlineData(400, 900, NavigationSuiteVariant.BottomBar)]    // phone portrait
		[InlineData(599.9, 900, NavigationSuiteVariant.BottomBar)]
		[InlineData(800, 400, NavigationSuiteVariant.BottomBar)]    // short window: bar even when wide
		[InlineData(600, 900, NavigationSuiteVariant.Rail)]
		[InlineData(700, 952, NavigationSuiteVariant.Rail)]         // gold medium capture
		[InlineData(960, 952, NavigationSuiteVariant.Rail)]         // gold: 840-1199 = rail + two-pane
		[InlineData(1199.9, 952, NavigationSuiteVariant.Rail)]
		[InlineData(1200, 952, NavigationSuiteVariant.PermanentDrawer)]
		[InlineData(1260, 952, NavigationSuiteVariant.PermanentDrawer)]
		public void NavigationSuite_VariantFor_FollowsGoldBreakpoints(
			double w, double h, NavigationSuiteVariant expected)
			=> Assert.Equal(expected, NavigationSuite.VariantFor(w, h));

		[Fact]
		public void NavigationSuite_SameSelectionContract_AndOwnContentBridging()
		{
			int invoked = -1;
			var selected = new Signal<int>(2);
			var suite = new NavigationSuite(selected,
				new[] { Item("a"), Item("b"), Item("c", () => invoked = 2) },
				content: new VStack { new Text("screen") },
				railHeader: new Icon("menu"));

			var node = Bridge(suite);
			// Own-content gating keys off the NODE type (platform nodes implement
			// IBackendManagesOwnContent; FakeBackendNode doesn't), so host-side the children
			// bridge normally: content + railHeader + 3 items.
			Assert.Equal(5, node.Children.Count);
			Assert.Equal(2, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);

			suite.SelectItem(2);
			Assert.Equal(2, invoked);
			selected.Value = 0;
			Assert.Equal(0, node.Get(PropertyIds.Nav_SelectedIndex).AsInt);
		}

		[Fact]
		public void NavigationSuite_ContentSwapsByReadingSelectedIndex()
		{
			// The screen-swap pattern the suite prescribes: content BINDINGS read the signal
			// (structure stays put — the body-swap node gap is exactly why).
			var selected = new Signal<int>(0);
			var titles = new[] { "Inbox", "Articles" };
			var content = new VStack { new Text(() => titles[selected.Value]) };
			var suite = new NavigationSuite(selected, new[] { Item("inbox"), Item("articles") }, content);
			Bridge(suite);

			// The suite's node owns content materialization on-device; host-side, bridge the
			// content directly to observe the binding patch flow.
			var contentNode = Bridge(content);
			Assert.Equal("Inbox", contentNode.Children[0].Get(PropertyIds.Text_Value).AsString);

			suite.SelectItem(1);
			ReactiveScheduler.FlushSync();
			Assert.Equal("Articles", contentNode.Children[0].Get(PropertyIds.Text_Value).AsString);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
