using System;
using System.Windows;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class LayoutHandler : WPFViewHandler<Layout, LayoutPanel>
	{
		public void Add(IView child)
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			var targetIndex = VirtualView.IndexOf(child);
			PlatformView.Children.Insert(targetIndex, (UIElement)child.ToPlatform(MauiContext));
		}

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);

			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;

			PlatformView.Children.Clear();

			foreach (var child in VirtualView)
			{
				PlatformView.Children.Add((UIElement)child.ToPlatform(MauiContext));
			}
		}

		public void Remove(IView child)
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");

			if ((child.Handler?.ContainerView ?? child.Handler?.PlatformView) is UIElement view)
			{
				PlatformView.Children.Remove(view);
			}
		}

		public void Clear()
		{
			PlatformView?.Children.Clear();
		}

		public void Insert(int index, IView child)
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			var targetIndex = VirtualView.IndexOf(child);
			PlatformView.Children.Insert(targetIndex, (UIElement)child.ToPlatform(MauiContext));
		}

		public void Update(int index, IView child)
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			PlatformView.Children[index] = (UIElement)child.ToPlatform(MauiContext);
			EnsureZIndexOrder(child);
		}

		public void UpdateZIndex(IView child)
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			EnsureZIndexOrder(child);
		}

		protected override LayoutPanel CreatePlatformView()
		{
			if (VirtualView == null)
			{
				throw new InvalidOperationException($"{nameof(VirtualView)} must be set to create a LayoutViewGroup");
			}

			var view = new LayoutPanel
			{
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange,
			};

			return view;
		}

		protected override void DisconnectHandler(LayoutPanel platformView)
		{
			// If we're being disconnected from the xplat element, then we should no longer be managing its children
			platformView.Children.Clear();
			base.DisconnectHandler(platformView);
		}

		void EnsureZIndexOrder(IView child)
		{
			if (PlatformView.Children.Count == 0)
			{
				return;
			}

			var currentIndex = PlatformView.Children.IndexOf((UIElement)child.ToPlatform(MauiContext!));

			if (currentIndex == -1)
			{
				return;
			}

			//var targetIndex = VirtualView.IndexOf(child);

			//if (currentIndex != targetIndex)
			//{
			//	PlatformView.Children.Move((uint)currentIndex, (uint)targetIndex);
			//}
		}

		static void MapInputTransparent(ILayoutHandler handler, ILayout layout)
		{
			//if (handler.PlatformView is LayoutPanel layoutPanel && layout != null)
			//{
			//	layoutPanel.UpdatePlatformViewBackground(layout);
			//}
		}
	}
}
