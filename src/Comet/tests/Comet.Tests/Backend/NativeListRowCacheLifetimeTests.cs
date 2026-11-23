#nullable enable
using System;
using System.Collections.Generic;
using Comet.Backend;
using Xunit;

namespace Comet.Tests.Backend;

public class NativeListRowCacheLifetimeTests : TestBase
{
	static readonly BackendContext Context = new(new Services());

	static NativeListRowCache Cache(int capacity) =>
		new(Context, _ => new FakeBackendNode()) { Capacity = capacity };

	[Fact]
	public void NativeRetainers_ProtectRowsBeyondBudget_UntilReleased()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		using var owner = new View();
		using var cache = Cache(1);
		var first = cache.GetOrCreate(0, owner, () => new Text("first"));
		var firstNode = (FakeBackendNode)first.Node!;
		var firstLease = cache.Retain(0, first);
		var second = cache.GetOrCreate(1, owner, () => new Text("second"));
		using var secondLease = cache.Retain(1, second);
		Assert.Equal(2, cache.Count);
		Assert.False(firstNode.Disposed);

		firstLease.Dispose();
		Assert.Equal(1, cache.Count);
		Assert.True(firstNode.Disposed);
		Assert.False(cache.TryGet(0, out _));
		Assert.True(cache.TryGet(1, out _));
	}

	[Fact]
	public void TwoNativeOwners_RequireBothToRelease()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		using var owner = new View();
		using var cache = Cache(1);
		var first = cache.GetOrCreate(0, owner, () => new Text("first"));
		var firstLease = cache.Retain(0, first);
		var secondLease = cache.Retain(0, first);
		var next = cache.GetOrCreate(1, owner, () => new Text("next"));
		using var nextLease = cache.Retain(1, next);
		firstLease.Dispose();
		firstLease.Dispose();
		Assert.True(cache.TryGet(0, out _));
		secondLease.Dispose();
		Assert.False(cache.TryGet(0, out _));
	}

	[Fact]
	public void ReleasedRows_EvictLeastRecentlyUsed_AndReleaseLogicalTemplate()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		using var list = new CollectionView<int>(() => new[] { 0, 1, 2 })
		{
			ViewFor = value => new Text(value.ToString()),
		};
		using var cache = Cache(2);
		cache.Evicted = list.ReleaseCachedRow;
		var source = (IListView)list;
		var firstContent = source.ViewFor(0, 0);
		var first = cache.GetOrCreate(0, list, () => firstContent);
		cache.Retain(0, first).Dispose();
		var secondContent = source.ViewFor(0, 1);
		var second = cache.GetOrCreate(1, list, () => secondContent);
		cache.Retain(1, second).Dispose();
		Assert.True(cache.TryGet(0, out _));
		var third = cache.GetOrCreate(2, list, () => source.ViewFor(0, 2));
		cache.Retain(2, third).Dispose();
		Assert.Equal(2, cache.Count);
		Assert.False(firstContent.IsDisposed);
		Assert.True(secondContent.IsDisposed);
		Assert.NotSame(secondContent, source.ViewFor(0, 1));
	}

	[Fact]
	public void OldGenerationLease_DoesNotUnpinReplacementRow()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		using var owner = new View();
		using var cache = Cache(1);
		var old = cache.GetOrCreate(0, owner, () => new Text("old"));
		var oldLease = cache.Retain(0, old);
		cache.InvalidateFrom(0);
		var replacement = cache.GetOrCreate(0, owner, () => new Text("replacement"));
		using var currentLease = cache.Retain(0, replacement);
		var other = cache.GetOrCreate(1, owner, () => new Text("other"));
		using var otherLease = cache.Retain(1, other);
		oldLease.Dispose();
		Assert.Equal(2, cache.Count);
		Assert.True(cache.TryGet(0, out var current));
		Assert.Same(replacement, current);
	}

	[Fact]
	public void DefaultBudget_PreservesStatefulRows()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		using var owner = new View();
		using var cache = Cache(0);
		for (int i = 0; i < 160; i++)
		{
			var row = cache.GetOrCreate(i, owner, () => new Text("stateful"));
			cache.Retain(i, row).Dispose();
		}
		Assert.Equal(160, cache.Count);
		Assert.True(cache.TryGet(0, out _));
	}

	[Fact]
	public void Limit_RejectsNegativeAndLateChanges()
	{
		using var list = new CollectionView<int>(() => new[] { 1 }) { RetainedItemLimit = 2 };
		Assert.Throws<ArgumentOutOfRangeException>(() => list.RetainedItemLimit = -1);
		CometBackendBridge.Materialize(list, _ => new FakeBackendNode(), Context);
		list.RetainedItemLimit = 2;
		Assert.Throws<InvalidOperationException>(() => list.RetainedItemLimit = 1);
	}

	sealed class Services : IServiceProvider
	{
		public object? GetService(Type type) => null;
	}
}
