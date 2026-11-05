#nullable enable
using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Backend;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Covers the data surface a Compose <c>LazyColumn</c> pulls from when rendering a
	/// Comet <c>ListView</c>: bridging produces a list node and signals composition, and
	/// the per-row template resolves to the expected views. (The virtualized rendering
	/// itself lives in the Android-only ComposeListNode.)
	/// </summary>
	public class BackendListTests
	{
		static BackendListTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		[Fact]
		public void ListView_BridgesAndSignalsComposition()
		{
			var list = new ListView<int>(() => Enumerable.Range(1, 5).ToList())
			{
				ViewFor = i => new Text($"Row {i}"),
			};

			var node = (FakeBackendNode)CometBackendBridge.Materialize(
				list, v => new FakeBackendNode("List"), Ctx);

			// The list node was told to (re)compose, and lazily-pulled rows are NOT
			// materialized as eager children.
			Assert.NotEqual(PropertyValueKind.None, node.Get(PropertyIds.List_Version).Kind);
			Assert.Empty(node.Children);
		}

		[Fact]
		public void ListView_RowTemplateResolvesPerIndex()
		{
			var list = new ListView<int>(() => Enumerable.Range(10, 3).ToList())
			{
				ViewFor = i => new Text($"Item {i}"),
			};

			var lv = (IListView)list;
			Assert.True(lv.Sections() >= 1);
			Assert.Equal(3, lv.Rows(0));
			Assert.IsType<Text>(lv.ViewFor(0, 0));
			Assert.IsType<Text>(lv.ViewFor(0, 2));
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
