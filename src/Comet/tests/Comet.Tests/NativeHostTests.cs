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

		[Fact]
		public void Sync_AppliesUpdates_WhenHandlerRequestsSynchronization()
		{
			var native = new NativeObject();
			var host = new NativeHost(native)
				.Sync("Ready", (view, text) => ((NativeObject)view).Text = text);

			var handler = new NativeHostTrackingHandler(native);
			host.ViewHandler = handler;

			Assert.Equal(1, handler.SyncCount);
			Assert.Equal("Ready", native.Text);
		}

		[Fact]
		public void TryGetNativeView_ReturnsHandlerHostedInstance()
		{
			var native = new NativeObject();
			var host = new NativeHost(native);
			host.ViewHandler = new NativeHostTrackingHandler(native);

			Assert.True(host.TryGetNativeView<NativeObject>(out var resolved));
			Assert.Same(native, resolved);
		}

		[Fact]
		public void MeasureUsing_TakesPrecedenceOverHandlerMeasurement()
		{
			var native = new NativeObject();
			var host = new NativeHost(native)
				.MeasureUsing(_ => new Size(80, 25));

			host.ViewHandler = new NativeHostTrackingHandler(native)
			{
				MeasuredSize = new Size(120, 60),
			};

			var measured = host.GetDesiredSize(new Size(200, 200));

			Assert.Equal(80, measured.Width);
			Assert.Equal(25, measured.Height);
		}

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

		class NativeHostTrackingHandler : IViewHandler, Comet.Handlers.INativeHostHandler
		{
			public NativeHostTrackingHandler(object nativeView)
			{
				NativeView = nativeView;
			}

			public IView CurrentView { get; private set; }
			public object NativeView { get; }
			public int SyncCount { get; private set; }
			public Size MeasuredSize { get; set; } = new Size(120, 35);
			public IMauiContext MauiContext => null;
			public object ContainerView => null;
			public bool HasContainer { get; set; }
			public Rect Frame { get; private set; }
			public Dictionary<string, object> ChangedProperties { get; } = new Dictionary<string, object>();

			IView IViewHandler.VirtualView => CurrentView;
			IElement IElementHandler.VirtualView => CurrentView;
			object IElementHandler.PlatformView => NativeView;

			public void SetVirtualView(IView view)
			{
				CurrentView = view;
			}

			void IElementHandler.SetVirtualView(IElement view) => SetVirtualView((IView)view);

			public void UpdateValue(string property)
			{
			}

			public void Invoke(string command, object args = null)
			{
			}

			public void DisconnectHandler()
			{
				CurrentView = null;
			}

			public void Dispose()
			{
			}

			void IElementHandler.SetMauiContext(IMauiContext mauiContext)
			{
			}

			Size IViewHandler.GetDesiredSize(double widthConstraint, double heightConstraint) => MeasuredSize;

			void IViewHandler.PlatformArrange(Rect frame)
			{
				Frame = frame;
			}

			object Comet.Handlers.INativeHostHandler.GetNativeView() => NativeView;

			Size Comet.Handlers.INativeHostHandler.MeasureNativeView(Size availableSize) => MeasuredSize;

			void Comet.Handlers.INativeHostHandler.SyncNativeView()
			{
				SyncCount++;
				if (CurrentView is NativeHost host)
					host.ApplyUpdated(NativeView, null);
			}
		}
	}
}
