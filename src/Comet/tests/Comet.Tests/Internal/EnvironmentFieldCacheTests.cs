using System;
using Comet.HotReload;
using Comet.Internal;
using Xunit;

namespace Comet.Tests;

public class EnvironmentFieldCacheTests : TestBase
{
	class BaseView : View
	{
		[Environment("Shared.Count")] public int count;
	}

	sealed class EnvironmentView : BaseView
	{
		[Environment] public string title;
	}

	[Fact]
	public void CachedFields_PreserveKeysAndInheritanceWithoutSharingInstanceState()
	{
		using var first = new EnvironmentView();
		using var second = new EnvironmentView();
		var fields = EnvironmentFieldCache.Get(first);
		Assert.Same(fields, EnvironmentFieldCache.Get(second));
		Assert.Contains(("count", "Shared.Count"), fields);
		Assert.Contains(("title", "title"), fields);
		first.SetEnvironment("Shared.Count", 3);
		first.SetEnvironment("title", "first");
		second.SetEnvironment("Shared.Count", 7);
		second.SetEnvironment("title", "second");
		first.Body = () => new Text(first.title);
		second.Body = () => new Text(second.title);
		_ = first.GetView();
		_ = second.GetView();
		Assert.Equal(3, first.count);
		Assert.Equal(7, second.count);
		Assert.Equal("first", first.title);
		Assert.Equal("second", second.title);
	}

	[Fact]
	public void MetadataUpdate_ClearsInheritedAndEmptyResults()
	{
		using var view = new EnvironmentView();
		using var empty = new Text("plain");
		var fields = EnvironmentFieldCache.Get(view);
		var emptyFields = EnvironmentFieldCache.Get(empty);
		Assert.Empty(emptyFields);
		CometMetadataUpdateHandler.ClearCache(new[] { typeof(BaseView) });
		Assert.NotSame(fields, EnvironmentFieldCache.Get(view));
		Assert.NotSame(emptyFields, EnvironmentFieldCache.Get(empty));
	}

	[Fact]
	public void WarmLookup_DoesNotRepeatReflectionAllocations()
	{
		using var view = new EnvironmentView();
		_ = EnvironmentFieldCache.Get(view);
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 100; i++)
			_ = EnvironmentFieldCache.Get(view);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.True(allocated < 1024, $"Warm metadata reads allocated {allocated} bytes.");
	}
}
