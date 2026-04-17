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
			InsertChildAt(targetIndex, child);
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

			// Realize each child independently so a failure in one doesn't break the rest of the layout.
			// We must keep PlatformView.Children index-aligned with VirtualView.Children so that
			// CrossPlatformArrange targets the right native element. If a child fails to realize,
			// we insert a transparent placeholder to preserve the index.
			foreach (var child in VirtualView)
			{
				PlatformView.Children.Add(RealizeOrPlaceholder(child));
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
			InsertChildAt(targetIndex, child);
		}

		public void Update(int index, IView child)
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			PlatformView.Children[index] = RealizeOrPlaceholder(child);
			EnsureZIndexOrder(child);
		}

		// Insert preserving the target index even if realization fails. This keeps
		// PlatformView.Children parallel to VirtualView.Children so cross-platform
		// arrange routes layout to the correct native element.
		void InsertChildAt(int index, IView child)
		{
			var element = RealizeOrPlaceholder(child);
			if (index < 0) index = 0;
			if (index > PlatformView.Children.Count) index = PlatformView.Children.Count;
			PlatformView.Children.Insert(index, element);
		}

		UIElement RealizeOrPlaceholder(IView child)
		{
			try
			{
				return (UIElement)child.ToPlatform(MauiContext!);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.WriteLine(
					$"[Microsoft.Maui.Platforms.Windows.WPF] LayoutHandler failed to realize child {child?.GetType().FullName}: {ex.Message}");
				// Transparent placeholder keeps UIElementCollection indices aligned with VirtualView.
				return new System.Windows.Controls.Border();
			}
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
				return;

			var nativeChild = (child.Handler?.ContainerView ?? child.Handler?.PlatformView) as UIElement;
			if (nativeChild is null)
				return;

			var currentIndex = PlatformView.Children.IndexOf(nativeChild);
			if (currentIndex == -1)
				return;

			var targetIndex = VirtualView.IndexOf(child);
			if (targetIndex < 0 || targetIndex == currentIndex)
				return;

			// UIElementCollection has no Move; emulate via Remove + Insert.
			PlatformView.Children.RemoveAt(currentIndex);
			if (targetIndex > PlatformView.Children.Count)
				targetIndex = PlatformView.Children.Count;
			PlatformView.Children.Insert(targetIndex, nativeChild);
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
