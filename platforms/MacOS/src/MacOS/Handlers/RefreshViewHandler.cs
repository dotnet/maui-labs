using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// RefreshView handler for macOS. Since macOS has no pull-to-refresh gesture,
/// this provides an overlay ActivityIndicator when IsRefreshing is true.
/// </summary>
public partial class RefreshViewHandler : MacOSViewHandler<RefreshView, NSView>
{
	public static readonly IPropertyMapper<RefreshView, RefreshViewHandler> Mapper =
		new PropertyMapper<RefreshView, RefreshViewHandler>(ViewMapper)
		{
			[nameof(RefreshView.IsRefreshing)] = MapIsRefreshing,
			[nameof(RefreshView.Content)] = MapContent,
			[nameof(RefreshView.RefreshColor)] = MapRefreshColor,
		};

	MacOSContainerView? _container;
	NSView? _contentView;
	NSProgressIndicator? _spinner;

	public RefreshViewHandler() : base(Mapper) { }

	protected override NSView CreatePlatformView()
	{
		_container = new MacOSContainerView();

		_spinner = new NSProgressIndicator(new CGRect(0, 0, 24, 24))
		{
			Style = NSProgressIndicatorStyle.Spinning,
			IsDisplayedWhenStopped = false,
			ControlSize = NSControlSize.Small,
		};
		_spinner.Hidden = true;
		_container.AddSubview(_spinner);

		return _container;
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);

		if (_contentView != null)
			_contentView.Frame = new CGRect(0, 0, rect.Width, rect.Height);

		if (_spinner != null)
		{
			var spinnerSize = 24;
			_spinner.Frame = new CGRect((rect.Width - spinnerSize) / 2, 8, spinnerSize, spinnerSize);
			// Bring spinner to front
			if (_spinner.Superview != null)
				_spinner.Superview.AddSubview(_spinner, NSWindowOrderingMode.Above, _contentView);
		}
	}

	public static void MapContent(RefreshViewHandler handler, RefreshView view)
	{
		handler.UpdateContent();
	}

	public static void MapIsRefreshing(RefreshViewHandler handler, RefreshView view)
	{
		handler.UpdateRefreshState();
	}

	public static void MapRefreshColor(RefreshViewHandler handler, RefreshView view)
	{
		// NSProgressIndicator doesn't easily support custom colors on macOS
	}

	void UpdateContent()
	{
		if (_container == null || MauiContext == null)
			return;

		_contentView?.RemoveFromSuperview();
		_contentView = null;

		if (VirtualView?.Content is IView content)
		{
			_contentView = content.ToMacOSPlatform(MauiContext);
			_container.AddSubview(_contentView, NSWindowOrderingMode.Below, _spinner);
		}
	}

	void UpdateRefreshState()
	{
		if (_spinner == null) return;

		if (VirtualView?.IsRefreshing == true)
		{
			_spinner.Hidden = false;
			_spinner.StartAnimation(null);
		}
		else
		{
			_spinner.StopAnimation(null);
			_spinner.Hidden = true;
		}
	}
}
