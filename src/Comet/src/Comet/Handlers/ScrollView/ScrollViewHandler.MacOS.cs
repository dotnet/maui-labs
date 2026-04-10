using System;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Comet.MacOS;

namespace Comet.Handlers
{
	public partial class ScrollViewHandler : ViewHandler<ScrollView, NSScrollView>
	{
		NSView _documentView;

		protected override NSScrollView CreatePlatformView()
		{
			var scrollView = new NSScrollView
			{
				HasVerticalScroller = true,
				HasHorizontalScroller = false,
				AutohidesScrollers = true,
				DrawsBackground = false,
			};
			_documentView = new NSView { AutoresizesSubviews = false };
			scrollView.DocumentView = _documentView;
			return scrollView;
		}

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);

			// Clear old content
			foreach (var sub in _documentView.Subviews)
				sub.RemoveFromSuperview();

			var content = VirtualView?.Content?.ToMacOSPlatform(MauiContext);
			if (content is not null)
				_documentView.AddSubview(content);

			if (VirtualView.Orientation == Orientation.Horizontal)
			{
				PlatformView.HasVerticalScroller = false;
				PlatformView.HasHorizontalScroller = true;
			}
			else
			{
				PlatformView.HasVerticalScroller = true;
				PlatformView.HasHorizontalScroller = false;
			}
		}
	}
}
