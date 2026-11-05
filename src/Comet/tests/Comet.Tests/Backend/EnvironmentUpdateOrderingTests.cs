#nullable enable
using System;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend;

public class EnvironmentUpdateOrderingTests : TestBase
{
	[Fact]
	public void FontChange_FlushObservesTheNewBackendValue()
	{
		ThreadHelper.SetFireOnMainThread(action => action());
		ReactiveScheduler.FlushSync();
		using var view = new Text("resize").FontSize(18);
		var node = (FakeBackendNode)CometBackendBridge.Materialize(
			view, _ => new FakeBackendNode(), new BackendContext(new Services()));
		int flushes = 0;
		double observedFontSize = 0;
		void Flushed()
		{
			flushes++;
			observedFontSize = node.Get(PropertyIds.Text_FontSize).AsDouble;
		}
		ReactiveScheduler.AfterFlush += Flushed;
		try
		{
			view.FontSize(30);
			Assert.Equal(1, flushes);
			Assert.Equal(30, observedFontSize);
		}
		finally
		{
			ReactiveScheduler.AfterFlush -= Flushed;
		}
	}

	sealed class Services : IServiceProvider
	{
		public object? GetService(Type type) => null;
	}
}
