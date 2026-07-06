#nullable enable
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks BODY-level window-metrics tracking — the adaptive NavigationSuite pattern reads
	/// WidthClass inside a [Body] and swaps chrome STRUCTURE (VStack+bottom bar vs HStack+rail).
	/// Binding-level tracking was locked in BackendWindowMetricsTests; this locks the body
	/// re-build path (View.GetRenderViewReactive dependencies → MarkViewDirty → Reload → Diff).
	/// </summary>
	public class BackendBodyMetricsTests
	{
		static BackendBodyMetricsTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		sealed class AdaptiveRoot : View
		{
			readonly CometWindowMetrics _metrics;
			public AdaptiveRoot(CometWindowMetrics metrics) => _metrics = metrics;

			[Body]
			View body() => _metrics.WidthClass == WindowWidthClass.Compact
				? new VStack { new Text("content"), new Text("bottombar") }
				: new HStack { new Text("rail"), new Text("content") };
		}

		[Fact(Skip = "KNOWN GAP (documented reproduction): the node backend does not propagate a " +
			"body-level container-TYPE swap — DiffUpdate returns the new view on type mismatch " +
			"(DatabindingExtensions.cs) and no node patches flow, so the old node tree keeps " +
			"rendering. View-level swap works (first half of this test); the node assert fails. " +
			"Adaptive chrome therefore ships as own-content nodes (NavigationSuite/ListDetail, " +
			"the SelectorPanel/Drawer idiom) instead of body-level composition. Un-skip when " +
			"generic subtree replacement lands.")]
		public void BodyReadingWidthClass_SwapsStructure_OnBreakpointCross()
		{
			var metrics = new CometWindowMetrics();
			metrics.Update(new Microsoft.Maui.Graphics.Size(400, 800));   // Compact

			var root = new AdaptiveRoot(metrics);
			var node = Bridge(root);
			Assert.Equal("VStack", node.Kind == "AdaptiveRoot" ? node.Children[0].Kind : node.Kind);

			metrics.Update(new Microsoft.Maui.Graphics.Size(700, 952));   // Medium — rail chrome
			ReactiveScheduler.FlushSync();

			// The built structure must now be the HStack variant. Walk from the materialized
			// root: either the root node re-pointed or its child was replaced.
			var built = root.BuiltView;
			Assert.NotNull(built);
			Assert.IsType<HStack>(built);

			// And the BACKEND tree must reflect it — the backend root keeps rendering the node
			// it materialized, so the swap has to arrive as patches on that same node tree
			// (this is exactly what the device exercises via ComposeView.SetContent).
			Assert.Contains("rail", AllTexts(node));
			Assert.DoesNotContain("bottombar", AllTexts(node));
		}

		static System.Collections.Generic.List<string> AllTexts(FakeBackendNode node)
		{
			var texts = new System.Collections.Generic.List<string>();
			Collect(node, texts);
			return texts;

			static void Collect(FakeBackendNode n, System.Collections.Generic.List<string> acc)
			{
				var v = n.Get(PropertyIds.Text_Value);
				if (v.AsString is { Length: > 0 } s)
					acc.Add(s);
				foreach (var c in n.Children)
					Collect(c, acc);
			}
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
