using System;
using Comet.HotReload;
using Comet.Internal;
using Xunit;

namespace Comet.Tests;

public class BodyMethodCacheTests : TestBase
{
	[Fact]
	public void GetBody_CachedMethod_BindsEachInstance()
	{
		using var first = new BodyView("first");
		using var second = new BodyView("second");
		var firstBody = first.GetBody();
		var secondBody = second.GetBody();

		Assert.Same(first, firstBody.Target);
		Assert.Same(second, secondBody.Target);
		Assert.Same(first.Content, firstBody());
		Assert.Same(second.Content, secondBody());
	}

	[Fact]
	public void GetBody_InheritedPrivateBody_IsPreserved()
	{
		using var view = new DerivedBodyView();
		Assert.Same(view.Content, view.GetBody()());
	}

	[Fact]
	public void GetBody_CachedMiss_DoesNotRepeatAttributeScanning()
	{
		using var view = new Text("plain");
		Assert.Null(view.GetBody());
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 100; i++)
			_ = view.GetBody();
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.True(allocated < 1024, $"Cached misses allocated {allocated} bytes.");
	}

	[Fact]
	public void ClearCache_InvalidatesMetadataWithoutRetainingAnInstanceDelegate()
	{
		using var view = new BodyView("body");
		var first = view.GetBody();
		CometMetadataUpdateHandler.ClearCache(new[] { typeof(BodyView) });
		var second = view.GetBody();
		Assert.NotSame(first, second);
		Assert.Same(view, second.Target);
		Assert.Same(view.Content, second());
	}

	class BodyView : View
	{
		public View Content { get; }
		public BodyView(string text) => Content = new Text(text);

		[Body]
		View BuildBody() => Content;
	}

	sealed class DerivedBodyView() : BodyView("inherited");
}
