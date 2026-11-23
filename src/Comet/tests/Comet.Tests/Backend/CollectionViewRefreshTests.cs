using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend;

public class CollectionViewRefreshTests : TestBase
{
	static readonly BackendContext Context = new(new Services());

	[Fact]
	public void RefreshItems_AppendBeforeFooter_KeepsExistingRowsAndDisposesFooter()
	{
		var first = new object();
		var footer = new object();
		IReadOnlyList<object> items = new[] { first, footer };
		using var list = new CollectionView<object>(() => items) { ViewFor = _ => new Text("row") };
		var node = (FakeBackendNode)CometBackendBridge.Materialize(list, _ => new FakeBackendNode(), Context);
		var source = (IListView)list;
		var firstView = source.ViewFor(0, 0);
		var footerView = source.ViewFor(0, 1);

		items = new[] { first, new object(), footer };
		list.RefreshItems();

		Assert.Equal(3, source.Rows(0));
		Assert.Same(firstView, source.ViewFor(0, 0));
		Assert.False(firstView.IsDisposed);
		Assert.True(footerView.IsDisposed);
		Assert.Equal(1, node.Get(PropertyIds.List_InvalidateFrom).AsInt);
		Assert.NotSame(footerView, source.ViewFor(0, 2));
	}

	[Fact]
	public void ReloadData_UnchangedItems_RebuildsTemplates()
	{
		using var list = new CollectionView<int>(() => new[] { 1 }) { ViewFor = _ => new Text("row") };
		var source = (IListView)list;
		var old = source.ViewFor(0, 0);
		list.ReloadData();
		Assert.True(old.IsDisposed);
		Assert.NotSame(old, source.ViewFor(0, 0));
	}

	[Fact]
	public void RefreshItems_RemoveAndReplace_InvalidatesChangedSuffix()
	{
		IReadOnlyList<int> items = new[] { 1, 2, 3 };
		using var list = new CollectionView<int>(() => items) { ViewFor = i => new Text($"{i}") };
		var source = (IListView)list;
		var old = Enumerable.Range(0, 3).Select(i => source.ViewFor(0, i)).ToArray();
		items = new[] { 1, 4 };
		list.RefreshItems();
		Assert.Same(old[0], source.ViewFor(0, 0));
		Assert.True(old[1].IsDisposed);
		Assert.True(old[2].IsDisposed);
		Assert.Equal(2, source.Rows(0));
	}

	[Fact]
	public void MaterializedRows_BeyondLegacyCacheLimit_KeepTheirTemplates()
	{
		using var list = new CollectionView<int>(() => Enumerable.Range(0, 160).ToArray())
		{
			ViewFor = _ => new Text("row"),
		};
		var source = (IListView)list;
		var first = source.ViewFor(0, 0);
		for (int i = 1; i < 160; i++)
			_ = source.ViewFor(0, i);
		Assert.False(first.IsDisposed);
		Assert.Same(first, source.ViewFor(0, 0));
	}

	[Fact]
	public void RowCache_InvalidatesOnlySuffix_AndDisposesEveryOwnedNode()
	{
		using var owner = new View();
		using var cache = new NativeListRowCache(Context, _ => new FakeBackendNode());
		var first = cache.GetOrCreate(0, owner, () => new VStack { new Text("first") });
		var second = cache.GetOrCreate(1, owner, () => new VStack { new Text("second") });
		var firstNode = (FakeBackendNode)first.Node;
		var secondNode = (FakeBackendNode)second.Node;
		var secondChild = secondNode.Children[0].Children[0];

		cache.InvalidateFrom(1);
		Assert.Same(first, cache.GetOrCreate(0, owner, () => throw new Exception("Cache missed")));
		Assert.False(firstNode.Disposed);
		Assert.True(secondNode.Disposed);
		Assert.True(secondChild.Disposed);
		Assert.False(cache.TryGet(1, out _));
		cache.Dispose();
		Assert.True(firstNode.Disposed);
	}

	[Fact]
	public void RowCache_ModifierWrites_FlushOnceAfterTheRowIsPublished()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		using var owner = new View();
		using var cache = new NativeListRowCache(Context, _ => new FakeBackendNode());
		int flushes = 0;
		bool published = false;
		void Flushed()
		{
			flushes++;
			published = cache.TryGet(0, out _);
		}
		ReactiveScheduler.AfterFlush += Flushed;
		try
		{
			cache.GetOrCreate(0, owner, () => new View()
				.SetEnvironment("RowCacheTest.First", 17)
				.SetEnvironment("RowCacheTest.Second", 18));
			Assert.Equal(1, flushes);
			Assert.True(published);
		}
		finally
		{
			ReactiveScheduler.AfterFlush -= Flushed;
		}
	}

	[Fact]
	public void Threshold_EnterAppendAndReenter_NotifyWithoutDuplicates()
	{
		IReadOnlyList<int> items = Enumerable.Range(0, 50).ToArray();
		using var list = new CollectionView<int>(() => items) { RemainingItemsThreshold = 5 };
		int calls = 0;
		int commandIndex = -1;
		list.RemainingItemsThresholdReached = () => calls++;
		list.RemainingItemsThresholdReachedCommand = index => commandIndex = index;
		list.NotifyVisibleIndex(43);
		Assert.Equal(0, calls);
		list.NotifyVisibleIndex(44);
		list.NotifyVisibleIndex(45);
		list.NotifyVisibleIndex(49);
		Assert.Equal(1, calls);
		Assert.Equal(44, commandIndex);
		items = Enumerable.Range(0, 100).ToArray();
		list.RefreshItems();
		list.NotifyVisibleIndex(49);
		Assert.Equal(1, calls);
		list.NotifyVisibleIndex(94);
		Assert.Equal(2, calls);
		list.NotifyVisibleIndex(30);
		list.NotifyVisibleIndex(94);
		Assert.Equal(3, calls);
	}

	[Theory]
	[InlineData(-1, 5, 4, 0)]
	[InlineData(0, 5, 3, 0)]
	[InlineData(0, 5, 4, 1)]
	[InlineData(5, 0, -1, 0)]
	[InlineData(5, 5, -1, 0)]
	[InlineData(5, 5, 5, 0)]
	public void Threshold_DisabledEmptyOrInvalidViewport_DoesNotNotify(
		int threshold, int count, int lastVisible, int expected)
	{
		using var list = new CollectionView<int>(() => Enumerable.Range(0, count).ToArray())
		{
			RemainingItemsThreshold = threshold,
		};
		int calls = 0;
		list.RemainingItemsThresholdReached = () => calls++;
		list.NotifyVisibleIndex(lastVisible);
		Assert.Equal(expected, calls);
	}

	[Fact]
	public void Threshold_ReentrantAppend_DoesNotRecursivelyLoad()
	{
		IReadOnlyList<int> items = new[] { 1 };
		using var list = new CollectionView<int>(() => items) { RemainingItemsThreshold = 5 };
		int calls = 0;
		list.RemainingItemsThresholdReached = () =>
		{
			calls++;
			items = new[] { 1, 2 };
			list.RefreshItems();
			list.NotifyVisibleIndex(1);
		};
		list.NotifyVisibleIndex(0);
		Assert.Equal(1, calls);
		list.NotifyVisibleIndex(1);
		Assert.Equal(2, calls);
	}

	sealed class Services : IServiceProvider
	{
		public object GetService(Type serviceType) => null;
	}
}
