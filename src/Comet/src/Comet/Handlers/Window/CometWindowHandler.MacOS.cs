using System;
using AppKit;
using CoreGraphics;
using Comet.MacOS;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	/// <summary>
	/// macOS-specific window handler for CometWindow. Creates a real NSWindow
	/// with AppKit chrome and hosts Comet view content via the handler chain.
	/// </summary>
	public partial class CometWindowHandler : ElementHandler<IWindow, NSWindow>
	{
		public static readonly IPropertyMapper<IWindow, CometWindowHandler> Mapper =
			new PropertyMapper<IWindow, CometWindowHandler>(ElementMapper)
			{
				[nameof(IWindow.Title)] = MapTitle,
				[nameof(IWindow.Content)] = MapContent,
			};

		CometWindowContentView _contentContainer;

		public CometWindowHandler() : base(Mapper)
		{
		}

		protected override NSWindow CreatePlatformElement()
		{
			var style = NSWindowStyle.Titled
				| NSWindowStyle.Closable
				| NSWindowStyle.Resizable
				| NSWindowStyle.Miniaturizable;

			var window = new NSWindow(
				new CGRect(0, 0, 1280, 720),
				style,
				NSBackingStore.Buffered,
				false);

			window.Center();
			#pragma warning disable CS0618 // ReleasedWhenClosed is obsolete
			window.ReleasedWhenClosed = false;
			#pragma warning restore CS0618

			_contentContainer = new CometWindowContentView();
			window.ContentView = _contentContainer;

			window.MakeKeyAndOrderFront(null);

			return window;
		}

		public static void MapTitle(CometWindowHandler handler, IWindow window)
		{
			if (handler.PlatformView is not null)
				handler.PlatformView.Title = window.Title ?? string.Empty;
		}

		public static void MapContent(CometWindowHandler handler, IWindow window)
		{
			if (handler.MauiContext is null || window.Content is null)
				return;

			var content = window.Content;
			var contentView = content.ToMacOSPlatform(handler.MauiContext);

			if (handler._contentContainer is not null)
			{
				foreach (var subview in handler._contentContainer.Subviews)
					subview.RemoveFromSuperview();

				contentView.Frame = handler._contentContainer.Bounds;
				contentView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
				handler._contentContainer.AddSubview(contentView);
				handler._contentContainer.HostedContent = new WeakReference<IView>(content);

				var bounds = handler._contentContainer.Bounds;
				if (bounds.Width > 0 && bounds.Height > 0)
				{
					content.Measure((double)bounds.Width, (double)bounds.Height);
					content.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
				}
			}
		}

		/// <summary>
		/// Flipped NSView used as NSWindow.ContentView so MAUI's top-left
		/// coordinate system works correctly with AppKit's bottom-left default.
		/// </summary>
		internal class CometWindowContentView : NSView
		{
			public CometWindowContentView()
			{
				WantsLayer = true;
				PostsFrameChangedNotifications = true;
			}

			public override bool IsFlipped => true;

			internal WeakReference<IView> HostedContent { get; set; }

			public override void SetFrameSize(CGSize newSize)
			{
				base.SetFrameSize(newSize);
				RelayoutContent();
			}

			public override void Layout()
			{
				base.Layout();
				RelayoutContent();
			}

			void RelayoutContent()
			{
				var size = Bounds.Size;
				if (size.Width <= 0 || size.Height <= 0)
					return;

				try
				{
					if (HostedContent is not null && HostedContent.TryGetTarget(out var content))
					{
						content.Measure((double)size.Width, (double)size.Height);
						content.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, (double)size.Width, (double)size.Height));
					}
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"[CometWindowContentView.RelayoutContent] {ex}");
				}
			}
		}
	}
}
