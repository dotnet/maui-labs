#nullable enable
using System;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend;

public class OwnedContentBatchingTests : TestBase
{
	[Fact]
	public void Materialize_ModifierWrites_FlushAfterSlotPublication()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		ReactiveScheduler.FlushSync();
		using var owner = new View();
		using var slot = new OwnedContentSlot<FakeBackendNode>(
			owner, _ => new FakeBackendNode(), new BackendContext(new Services()));
		using var content = new View
		{
			Body = () => new Text("content")
				.SetEnvironment("OwnedBatch.First", 1)
				.SetEnvironment("OwnedBatch.Second", 2),
		};
		int flushes = 0;
		bool published = false;
		void Flushed()
		{
			flushes++;
			published = slot.TryGet(out var node, out var view) &&
				node is not null && ReferenceEquals(view, content);
		}
		ReactiveScheduler.AfterFlush += Flushed;
		try
		{
			slot.Materialize(content);
			Assert.Equal(1, flushes);
			Assert.True(published);
		}
		finally
		{
			ReactiveScheduler.AfterFlush -= Flushed;
		}
	}

	[Fact]
	public void Materialize_Failure_ReleasesPartialGenerationAndAllowsRetry()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		using var owner = new View();
		FakeBackendNode? partial = null;
		bool fail = true;
		using var slot = new OwnedContentSlot<FakeBackendNode>(owner, view =>
		{
			if (view is Text && fail)
				throw new InvalidOperationException("test");
			return partial = new FakeBackendNode();
		}, new BackendContext(new Services()));
		using var content = new VStack { new Text("child") };
		Assert.Throws<InvalidOperationException>(() => slot.Materialize(content));
		Assert.True(partial!.Disposed);
		Assert.False(slot.TryGet(out _, out _));
		fail = false;
		Assert.NotNull(slot.Materialize(content));
		Assert.True(slot.TryGet(out _, out _));
	}

	sealed class Services : IServiceProvider
	{
		public object? GetService(Type type) => null;
	}
}
