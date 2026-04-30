using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
public partial class CometViewHandler : ViewHandler<View, Comet.Windows.CometView>
{
public static PropertyMapper<View, CometViewHandler> CometViewMapper = new()
{
[nameof(ITitledElement.Title)] = MapTitle,
[nameof(IView.Background)] = MapBackgroundColor,
};

public CometViewHandler() : base(CometViewMapper)
{
}

protected override Comet.Windows.CometView CreatePlatformView() => new(MauiContext);

public override void SetVirtualView(IView view)
{
base.SetVirtualView(view);
PlatformView.CurrentView = view;
}

public static void MapTitle(CometViewHandler handler, View view)
{
// Windows doesn't have a direct title mapping at the Grid level
// Title is typically set on the window or page, not individual views
// This is a no-op for grid-based views
}

public static void MapBackgroundColor(CometViewHandler handler, View view)
{
if (handler?.PlatformView is null)
return;

var background = ((IView)view).Background;
if (background is SolidPaint solid && solid.Color is not null)
{
var color = new global::Windows.UI.Color
{
A = (byte)(solid.Color.Alpha * 255),
R = (byte)(solid.Color.Red * 255),
G = (byte)(solid.Color.Green * 255),
B = (byte)(solid.Color.Blue * 255)
};
handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
}
else if (background is null)
{
handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
Microsoft.UI.Colors.White);
}
}
}
}
