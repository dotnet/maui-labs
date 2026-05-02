using System;
using AppKit;
using Comet.MacOS;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	public partial class ShapeViewHandler : ViewHandler<ShapeView, CUIShapeNSView>
	{
		protected override CUIShapeNSView CreatePlatformView() => new CUIShapeNSView();

		public static void MapShapeProperty(IElementHandler viewHandler, ShapeView virtualView)
		{
			var nativeView = (CUIShapeNSView)viewHandler.PlatformView;
			var shape = virtualView.Shape?.CurrentValue;
			nativeView.View = virtualView;
			nativeView.Shape = shape;
		}
	}
}
