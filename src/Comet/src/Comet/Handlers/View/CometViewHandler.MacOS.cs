using System;
using Comet.MacOS;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

namespace Comet.Handlers
{
	public partial class CometViewHandler : ViewHandler<View, CometNSView>
	{
		public static PropertyMapper<View, CometViewHandler> CometViewMapper = new()
		{
			[nameof(ITitledElement.Title)] = MapTitle,
			[nameof(IView.Background)] = MapBackgroundColor,
		};

		public CometViewHandler() : base(CometViewMapper)
		{
		}

		protected override CometNSView CreatePlatformView() => new CometNSView(MauiContext);

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);
			PlatformView.CurrentView = view;
		}

		public static void MapTitle(CometViewHandler handler, View view)
		{
			// macOS title is managed at the window level, not per-view
		}

		public static void MapBackgroundColor(CometViewHandler handler, View view)
		{
			handler.PlatformView?.UpdateBackground(view);
		}
	}
}
