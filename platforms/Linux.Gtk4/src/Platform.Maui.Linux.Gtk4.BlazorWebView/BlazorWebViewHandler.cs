using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Platform.Maui.Linux.Gtk4.BlazorWebView;

public class BlazorWebViewHandler : ViewHandler<IBlazorWebView, Gtk.Box>
{
	private GtkBlazorWebView? _blazorWebView;

	public static IPropertyMapper<IBlazorWebView, BlazorWebViewHandler> BlazorWebViewMapper =
		new PropertyMapper<IBlazorWebView, BlazorWebViewHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IBlazorWebView.HostPage)] = MapHostPage,
			[nameof(IBlazorWebView.RootComponents)] = MapRootComponents,
		};

	public static CommandMapper<IBlazorWebView, BlazorWebViewHandler> BlazorWebViewCommandMapper =
		new(ViewHandler.ViewCommandMapper)
		{
			["Focus"] = MapFocus,
		};

	public BlazorWebViewHandler() : base(BlazorWebViewMapper, BlazorWebViewCommandMapper)
	{
	}

	protected override Gtk.Box CreatePlatformView()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		box.SetVexpand(true);
		box.SetHexpand(true);

		_blazorWebView = new GtkBlazorWebView(MauiContext!.Services);
		_blazorWebView.Widget.SetVexpand(true);
		_blazorWebView.Widget.SetHexpand(true);
		_blazorWebView.Widget.SetSizeRequest(400, 300); // Minimum size
		box.Append(_blazorWebView.Widget);

		return box;
	}

	protected override void ConnectHandler(Gtk.Box platformView)
	{
		base.ConnectHandler(platformView);

		// Properties may already be set before handler connected — trigger them now
		// DevToys pattern: add root components BEFORE setting HostPage
		if (VirtualView?.RootComponents?.Count > 0)
			MapRootComponents(this, VirtualView);
		if (VirtualView?.HostPage != null)
			MapHostPage(this, VirtualView);
	}

	protected override void DisconnectHandler(Gtk.Box platformView)
	{
		_blazorWebView?.Dispose();
		_blazorWebView = null;
		base.DisconnectHandler(platformView);
	}

	public override void PlatformArrange(Rect rect)
	{
		try
		{
			var view = PlatformView;
			var height = (int)rect.Height;
			var width = (int)rect.Width;

			// MAUI may arrange with the full window size, but the GTK content area
			// (below the HeaderBar/titlebar) is smaller. Walk up to the window's
			// direct child container and clamp to its allocated height.
			Gtk.Widget? ancestor = view.GetParent();
			while (ancestor != null)
			{
				if (ancestor.GetParent() is Gtk.Window)
				{
					int containerH = ancestor.GetAllocatedHeight();
					if (containerH > 0)
					{
						int maxH = containerH - (int)rect.Y;
						if (maxH > 0 && maxH < height)
							height = maxH;
					}
					break;
				}
				ancestor = ancestor.GetParent();
			}

			if (view.GetParent() is global::Platform.Maui.Linux.Gtk4.Platform.GtkLayoutPanel layoutPanel)
			{
				layoutPanel.SetChildBounds(view, rect.X, rect.Y, width, height);
			}
			else
			{
				view.SetSizeRequest(width, height);
			}
		}
		catch (InvalidOperationException)
		{
			// PlatformView not ready yet
		}
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		try { _ = PlatformView; } catch { return new Size(widthConstraint, 400); }
		// BlazorWebView should fill available space
		if (VirtualView is Microsoft.Maui.Controls.VisualElement ve)
		{
			var h = ve.HeightRequest >= 0 ? ve.HeightRequest : heightConstraint;
			var w = ve.WidthRequest >= 0 ? ve.WidthRequest : widthConstraint;
			return new Size(Math.Min(w, widthConstraint), Math.Min(h, heightConstraint));
		}
		return new Size(widthConstraint, Math.Min(400, heightConstraint));
	}

	public static void MapHostPage(BlazorWebViewHandler handler, IBlazorWebView webView)
	{
		if (handler._blazorWebView != null && webView.HostPage != null)
		{
			// Transfer StartPath before setting HostPage (which triggers navigation)
			if (webView is Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView mauiWebView
				&& !string.IsNullOrEmpty(mauiWebView.StartPath))
			{
				handler._blazorWebView.StartPath = mauiWebView.StartPath;
			}

			handler._blazorWebView.HostPage = webView.HostPage;
		}
	}

	public static void MapRootComponents(BlazorWebViewHandler handler, IBlazorWebView webView)
	{
		if (handler._blazorWebView == null)
			return;

		handler._blazorWebView.RootComponents.Clear();
		foreach (var rc in webView.RootComponents)
		{
			handler._blazorWebView.RootComponents.Add(new RootComponent
			{
				Selector = rc.Selector,
				ComponentType = rc.ComponentType,
				Parameters = rc.Parameters,
			});
		}
	}

	static void MapFocus(BlazorWebViewHandler handler, IBlazorWebView view, object? args)
	{
		if (args is Microsoft.Maui.RetrievePlatformValueRequest<bool> request)
		{
			try
			{
				var result = handler.PlatformView?.GrabFocus() ?? false;
				request.SetResult(result);
			}
			catch
			{
				request.SetResult(false);
			}
		}
	}
}
