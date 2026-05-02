using System;
using CoreGraphics;
using UIKit;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Comet.Handlers
{
public partial class MauiViewHostHandler : ViewHandler<MauiViewHost, MauiViewHostHandler.MauiViewHostContainerView>
{
public static IPropertyMapper<MauiViewHost, MauiViewHostHandler> Mapper =
new PropertyMapper<MauiViewHost, MauiViewHostHandler>(ViewHandler.ViewMapper);

public MauiViewHostHandler() : base(Mapper) { }

protected override MauiViewHostContainerView CreatePlatformView()
=> new MauiViewHostContainerView();

protected override void ConnectHandler(MauiViewHostContainerView platformView)
{
base.ConnectHandler(platformView);
UpdateHostedView();
}

protected override void DisconnectHandler(MauiViewHostContainerView platformView)
{
if (VirtualView?.HostedView?.Handler is IElementHandler hostedHandler)
{
hostedHandler.DisconnectHandler();
if (hostedHandler is IDisposable disposableHandler)
disposableHandler.Dispose();
}
platformView.ClearHostedView();
base.DisconnectHandler(platformView);
}

void UpdateHostedView()
{
if (VirtualView?.HostedView is null || MauiContext is null)
return;

try
{
UIView hostedPlatformView = null;

// Try primary MauiContext first
try
{
hostedPlatformView = VirtualView.HostedView.ToPlatform(MauiContext);
}
catch (Exception)
{
// Handler not found in this MauiContext - try CometApp's context
// which has all registered handlers including third-party (Syncfusion, etc.)
var fallbackCtx = CometApp.MauiContext;
if (fallbackCtx is not null && fallbackCtx != MauiContext)
{
try
{
hostedPlatformView = VirtualView.HostedView.ToPlatform(fallbackCtx);
}
catch (Exception ex2)
{
Console.WriteLine($"[MauiViewHostHandler] All ToPlatform failed for {VirtualView.HostedView.GetType().Name}: {ex2.Message}");
}
}
}

if (hostedPlatformView is not null)
PlatformView.SetHostedView(hostedPlatformView, VirtualView.HostedView);
}
catch (Exception ex)
{
Console.WriteLine($"[MauiViewHostHandler] UpdateHostedView failed: {ex.Message}");
}
}

public class MauiViewHostContainerView : UIView
{
UIView _hostedPlatformView;
IView _hostedVirtualView;

public void SetHostedView(UIView platformView, IView virtualView)
{
_hostedPlatformView?.RemoveFromSuperview();
_hostedPlatformView = platformView;
_hostedVirtualView = virtualView;
if (_hostedPlatformView is not null)
{
AddSubview(_hostedPlatformView);
SetNeedsLayout();
}
}

public void ClearHostedView()
{
_hostedPlatformView?.RemoveFromSuperview();
_hostedPlatformView = null;
_hostedVirtualView = null;
}

public override void LayoutSubviews()
{
base.LayoutSubviews();
if (_hostedPlatformView is null || Bounds.Width <= 0 || Bounds.Height <= 0)
return;

// Use MAUI's cross-platform layout to arrange children
var bounds = new Microsoft.Maui.Graphics.Rect(0, 0, Bounds.Width, Bounds.Height);
_hostedVirtualView?.Measure(Bounds.Width, Bounds.Height);
_hostedVirtualView?.Arrange(bounds);

// Also set the platform frame directly as a fallback
_hostedPlatformView.Frame = Bounds;
}

public override CGSize SizeThatFits(CGSize size)
{
if (_hostedVirtualView is not null)
{
var measured = _hostedVirtualView.Measure(size.Width, size.Height);
return new CGSize(measured.Width, measured.Height);
}
if (_hostedPlatformView is not null)
return _hostedPlatformView.SizeThatFits(size);
return base.SizeThatFits(size);
}

public override CGSize IntrinsicContentSize
{
get
{
if (_hostedPlatformView is not null)
return _hostedPlatformView.IntrinsicContentSize;
return base.IntrinsicContentSize;
}
}
}
}
}
