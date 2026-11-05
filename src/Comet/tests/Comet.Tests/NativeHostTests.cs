using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class NativeHostTests : TestBase
	{
		[Fact]
		public void Constructor_NullFactory_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new NativeHost((Func<IMauiContext, object>)null));
		}

		[Fact]
		public void Constructor_NullNativeView_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new NativeHost((object)null));
		}

		[Fact]
		public void Factory_DefersCreation_And_CachesNativeView()
		{
			var createCount = 0;
			var host = new NativeHost(_ =>
			{
				createCount++;
				return new NativeObject();
			});

			Assert.Equal(0, createCount);

			var first = host.GetOrCreateNativeView(null);
			var second = host.GetOrCreateNativeView(null);

			Assert.Equal(1, createCount);
			Assert.Same(first, second);
		}

		[Fact]
		public void ReleaseNativeView_ClearsOwnedFactoryInstance()
		{
			var host = new NativeHost(_ => new NativeObject(), ownsNativeView: true);

			var first = host.GetOrCreateNativeView(null);
			host.ReleaseNativeView(first, disposed: true);
			var second = host.GetOrCreateNativeView(null);

			Assert.NotSame(first, second);
		}

		// The handler-driven Sync/TryGetNativeView/MeasureUsing-precedence tests were
		// deleted with the legacy MAUI ViewHandler render path (Phase 5): they exercised
		// Comet.Handlers.INativeHostHandler, which no longer exists.

		[Fact]
		public void OnConnectAndDisconnect_RunLifecycleCallbacks()
		{
			var events = new List<string>();
			var host = new NativeHost(new NativeObject())
				.OnConnect((_, __) => events.Add("connect"))
				.OnDisconnect(_ => events.Add("disconnect"));

			host.ApplyConnected(new NativeObject(), null);
			host.ApplyDisconnected(new NativeObject());

			Assert.Equal(new[] { "connect", "disconnect" }, events);
		}

		class NativeObject
		{
			public string Text { get; set; }
		}

	}
}
