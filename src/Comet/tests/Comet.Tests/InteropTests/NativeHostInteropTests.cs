using System;
using System.Collections.Generic;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class NativeHostInteropTests : TestBase
	{
		[Fact]
		public void MauiViewHost_UsesHostedMeasurementWhenHandlerExists()
		{
			var hosted = new MeasuringMauiView
			{
				NextSize = new Size(180, 45),
				Handler = new GenericViewHandler()
			};
			var host = new MauiViewHost(hosted);

			var size = host.GetDesiredSize(new Size(300, 200));

			Assert.Equal(180, size.Width);
			Assert.Equal(45, size.Height);
			Assert.Equal(1, hosted.MeasureCallCount);
		}

		[Fact]
		public void MauiViewHost_FallsBackWhenHostedMeasurementThrows()
		{
			var hosted = new MeasuringMauiView
			{
				ThrowOnMeasure = true,
				Handler = new GenericViewHandler()
			};
			var host = new MauiViewHost(hosted).Frame(height: 55);

			var size = host.GetDesiredSize(new Size(240, 180));

			Assert.Equal(240, size.Width);
			Assert.Equal(55, size.Height);
			Assert.Equal(1, hosted.MeasureCallCount);
		}

		[Fact]
		public void MauiViewHost_AddsMarginsAfterHostedMeasurement()
		{
			var hosted = new MeasuringMauiView
			{
				NextSize = new Size(120, 30),
				Handler = new GenericViewHandler()
			};
			var host = new MauiViewHost(hosted)
				.Margin(left: 8, top: 4, right: 12, bottom: 6);

			var size = host.GetDesiredSize(new Size(300, 200));

			Assert.Equal(140, size.Width);
			Assert.Equal(40, size.Height);
		}

		[Fact]
		public void MauiViewHost_Dispose_DoesNotRealizeDeferredFactory()
		{
			var created = false;
			var host = new MauiViewHost(() =>
			{
				created = true;
				return new DisposableMeasuringMauiView();
			});

			host.Dispose();

			Assert.False(created);
			Assert.Null(host.HostedView);
		}

		[Fact]
		public void MauiViewHost_Dispose_CleansUpFactoryCreatedHostedView()
		{
			var hosted = new DisposableMeasuringMauiView();
			var host = new MauiViewHost(() => hosted);

			Assert.Same(hosted, host.HostedView);

			host.Dispose();

			Assert.True(hosted.IsDisposed);
			Assert.Null(host.HostedView);
		}

		[Fact]
		public void MauiViewHost_FactoryException_IsNotRetried()
		{
			var attempts = 0;
			var host = new MauiViewHost(() =>
			{
				attempts++;
				throw new InvalidOperationException("boom");
			});

			Assert.Throws<InvalidOperationException>(() => _ = host.HostedView);
			Assert.Null(host.HostedView);
			Assert.Equal(1, attempts);
		}

		[Fact]
		public void CometHost_DefaultConstructor_LeavesCometViewUnset()
		{
			var host = new CometHost();

			Assert.Null(host.CometView);
		}

		[Fact]
		public void CometHost_CometViewProperty_UsesCometViewType()
		{
			Assert.Equal(nameof(CometHost.CometView), CometHost.CometViewProperty.PropertyName);
			Assert.Equal(typeof(View), CometHost.CometViewProperty.ReturnType);
			Assert.Equal(typeof(CometHost), CometHost.CometViewProperty.DeclaringType);
		}

		[Fact]
		public void CometHost_CometViewProperty_CanBeClearedToNull()
		{
			var host = new CometHost(new Text("Host"));

			host.ClearValue(CometHost.CometViewProperty);

			Assert.Null(host.CometView);
		}

		[Fact]
		public void GetView_CachesInteropBridgeBody()
		{
			var page = new InteropPage();

			var first = page.GetView();
			var second = page.GetView();

			Assert.IsType<MauiViewHost>(first);
			Assert.Same(first, second);
			Assert.Equal(1, page.BuildCount);
		}

		[Fact]
		public void CometHost_CanWrapComponentThatRendersMauiViewHost()
		{
			var component = new InteropComponent();
			var host = new CometHost(component);

			var rendered = host.CometView.GetView();

			Assert.IsType<MauiViewHost>(rendered);
			Assert.Equal(1, component.RenderCount);
		}

		[Fact]
		public void NativeHost_CanWrapImmediateNativeControl()
		{
			var native = new NativeObject();
			var host = new NativeHost(native, ownsNativeView: false);
			var stack = new HStack
			{
				host,
				new Text("Comet")
			};

			Assert.Same(native, host.GetOrCreateNativeView(null));
			Assert.Same(stack, host.Parent);
		}

		[Fact]
		public void NativeHost_Factory_IsLazyAndCached()
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
		public void NativeHost_CanParticipateInMixedInteropLayouts()
		{
			var nativeHost = new NativeHost(new NativeObject())
				.MeasureUsing(_ => new Size(90, 30));
			var mauiHost = new MauiViewHost(new MeasuringMauiView
			{
				NextSize = new Size(70, 20),
				Handler = new GenericViewHandler()
			});
			var stack = new HStack
			{
				nativeHost,
				new Text("Comet"),
				mauiHost
			};

			var nativeSize = nativeHost.GetDesiredSize(new Size(300, 200));
			var mauiSize = mauiHost.GetDesiredSize(new Size(300, 200));

			Assert.Same(stack, nativeHost.Parent);
			Assert.Same(stack, mauiHost.Parent);
			Assert.Equal(90, nativeSize.Width);
			Assert.Equal(30, nativeSize.Height);
			Assert.Equal(70, mauiSize.Width);
			Assert.Equal(20, mauiSize.Height);
		}

		[Fact]
		public void NativeHost_ExposesNativeViewAccessAfterHandlerInitialization()
		{
			var native = new NativeObject();
			var host = new NativeHost(native);
			host.ViewHandler = new NativeHostTrackingHandler(native);

			Assert.True(host.TryGetNativeView<NativeObject>(out var resolved));
			Assert.Same(native, resolved);
		}

		class InteropPage : View
		{
			public int BuildCount { get; private set; }

			[Body]
			View body()
			{
				BuildCount++;
				return new MauiViewHost(new MeasuringMauiView());
			}
		}

		class InteropComponent : Component
		{
			public int RenderCount { get; private set; }

			public override View Render()
			{
				RenderCount++;
				return new MauiViewHost(new MeasuringMauiView());
			}
		}

		class MeasuringMauiView : IView
		{
			public int MeasureCallCount { get; private set; }
			public Size NextSize { get; set; } = new Size(100, 40);
			public bool ThrowOnMeasure { get; set; }

			public string AutomationId => string.Empty;
			public FlowDirection FlowDirection => FlowDirection.LeftToRight;
			Microsoft.Maui.Primitives.LayoutAlignment IView.HorizontalLayoutAlignment => Microsoft.Maui.Primitives.LayoutAlignment.Fill;
			Microsoft.Maui.Primitives.LayoutAlignment IView.VerticalLayoutAlignment => Microsoft.Maui.Primitives.LayoutAlignment.Fill;
			public Semantics Semantics => null;
			public IShape Clip => null;
			public IShadow Shadow => null;
			public bool IsEnabled => true;
			public bool IsFocused { get; set; }
			public Visibility Visibility => Visibility.Visible;
			public double Opacity => 1;
			public Paint Background => null;
			public Rect Frame { get; set; }
			public double Width => -1;
			public double MinimumWidth => -1;
			public double MaximumWidth => -1;
			public double Height => -1;
			public double MinimumHeight => -1;
			public double MaximumHeight => -1;
			public Thickness Margin => Thickness.Zero;
			public Size DesiredSize => NextSize;
			public int ZIndex => 0;
			public bool InputTransparent => false;
			public double TranslationX => 0;
			public double TranslationY => 0;
			public double Scale => 1;
			public double ScaleX => 1;
			public double ScaleY => 1;
			public double Rotation => 0;
			public double RotationX => 0;
			public double RotationY => 0;
			public double AnchorX => 0.5;
			public double AnchorY => 0.5;
			public IViewHandler Handler { get; set; }
			IElementHandler IElement.Handler { get; set; }
			public IElement Parent => null;

			public Size Arrange(Rect bounds) => bounds.Size;

			public bool Focus() => false;

			public void InvalidateArrange()
			{
			}

			public void InvalidateMeasure()
			{
			}

			public Size Measure(double widthConstraint, double heightConstraint)
			{
				MeasureCallCount++;
				if (ThrowOnMeasure)
					throw new InvalidOperationException("measure failed");

				return NextSize;
			}

			public void Unfocus()
			{
			}
		}

		class DisposableMeasuringMauiView : MeasuringMauiView, IDisposable
		{
			public bool IsDisposed { get; private set; }

			public void Dispose()
			{
				IsDisposed = true;
			}
		}

		class NativeObject
		{
		}

		class NativeHostTrackingHandler : IViewHandler, Comet.Handlers.INativeHostHandler
		{
			public NativeHostTrackingHandler(object nativeView)
			{
				NativeView = nativeView;
			}

			public IView CurrentView { get; private set; }
			public object NativeView { get; }
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

			Size IViewHandler.GetDesiredSize(double widthConstraint, double heightConstraint) => new Size(120, 35);

			void IViewHandler.PlatformArrange(Rect frame)
			{
				Frame = frame;
			}

			object Comet.Handlers.INativeHostHandler.GetNativeView() => NativeView;

			Size Comet.Handlers.INativeHostHandler.MeasureNativeView(Size availableSize) => new Size(120, 35);

			void Comet.Handlers.INativeHostHandler.SyncNativeView()
			{
				if (CurrentView is NativeHost host)
					host.ApplyUpdated(NativeView, null);
			}
		}
	}
}
