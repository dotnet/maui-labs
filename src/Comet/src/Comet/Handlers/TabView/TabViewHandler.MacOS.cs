using System;
using System.Linq;
using AppKit;
using Comet.MacOS;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
namespace Comet.Handlers
{
	public partial class TabViewHandler : ViewHandler<TabView, CUITabNSView>
	{
		protected override CUITabNSView CreatePlatformView() => new CUITabNSView { Context = MauiContext };

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);
			PlatformView?.Setup(this.VirtualView);
		}
	}
}
