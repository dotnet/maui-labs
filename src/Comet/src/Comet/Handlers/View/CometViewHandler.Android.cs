using System;
using Comet.Android;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
namespace Comet.Handlers
{
	public partial class CometViewHandler : ViewHandler<View, CometView>
	{
		public static PropertyMapper<View, CometViewHandler> CometViewMapper = new()
		{
		};

		public CometViewHandler() : base(CometViewMapper)
		{
		}

		protected override CometView CreatePlatformView() => new CometView(MauiContext);

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);
			PlatformView.CurrentView = view;
		}
	}
}
