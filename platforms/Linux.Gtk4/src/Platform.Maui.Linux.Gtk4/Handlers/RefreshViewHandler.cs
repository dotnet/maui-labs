using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Handler for RefreshView. On desktop Linux, pull-to-refresh is not natural,
/// so this provides a refresh button at the top and IsRefreshing spinner overlay.
/// </summary>
public class RefreshViewHandler : GtkViewHandler<IView, Gtk.Box>
{
	Gtk.Spinner? _spinner;
	Gtk.Button? _refreshButton;
	Gtk.Widget? _contentWidget;

	public static new IPropertyMapper<IView, RefreshViewHandler> Mapper =
		new PropertyMapper<IView, RefreshViewHandler>(ViewMapper)
		{
			["Content"] = MapContent,
			["IsRefreshing"] = MapIsRefreshing,
			["RefreshColor"] = MapRefreshColor,
			["IsEnabled"] = MapIsEnabled,
		};

	public static CommandMapper<IView, RefreshViewHandler> CommandMapper = new(ViewCommandMapper);

	public RefreshViewHandler() : base(Mapper, CommandMapper) { }

	protected override Gtk.Box CreatePlatformView()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		box.SetHexpand(true);

		// Refresh indicator row
		var refreshRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
		refreshRow.SetHalign(Gtk.Align.Center);
		refreshRow.SetMarginTop(4);
		refreshRow.SetMarginBottom(4);

		_spinner = Gtk.Spinner.New();
		_spinner.SetVisible(false);
		refreshRow.Append(_spinner);

		_refreshButton = Gtk.Button.NewFromIconName("view-refresh-symbolic");
		_refreshButton.SetTooltipText("Refresh");
		_refreshButton.OnClicked += OnRefreshClicked;
		refreshRow.Append(_refreshButton);

		box.Append(refreshRow);
		return box;
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		if (_contentWidget != null)
		{
			_contentWidget.SetSizeRequest((int)rect.Width, (int)rect.Height - 40);
		}
	}

	void OnRefreshClicked(Gtk.Button sender, EventArgs args)
	{
		if (VirtualView is RefreshView rv && rv.Command?.CanExecute(rv.CommandParameter) == true)
		{
			rv.IsRefreshing = true;
			rv.Command.Execute(rv.CommandParameter);
		}
	}

	public static void MapContent(RefreshViewHandler handler, IView view)
	{
		if (view is not RefreshView refreshView || handler.MauiContext == null)
			return;

		// Remove old content
		if (handler._contentWidget != null)
		{
			handler.PlatformView.Remove(handler._contentWidget);
			handler._contentWidget = null;
		}

		if (refreshView.Content != null)
		{
			var platformContent = (Gtk.Widget)refreshView.Content.ToPlatform(handler.MauiContext);
			platformContent.SetVexpand(true);
			platformContent.SetHexpand(true);
			handler._contentWidget = platformContent;
			handler.PlatformView.Append(platformContent);
		}
	}

	public static void MapIsRefreshing(RefreshViewHandler handler, IView view)
	{
		if (view is not RefreshView refreshView)
			return;

		handler._spinner?.SetSpinning(refreshView.IsRefreshing);
		handler._spinner?.SetVisible(refreshView.IsRefreshing);
	}

	public static void MapRefreshColor(RefreshViewHandler handler, IView view)
	{
		if (view is RefreshView rv && rv.RefreshColor != null && handler._spinner != null)
			handler.ApplyCss(handler._spinner, $"color: {ToGtkColor(rv.RefreshColor)};");
	}

	public static void MapIsEnabled(RefreshViewHandler handler, IView view)
	{
		handler._refreshButton?.SetSensitive(view.IsEnabled);
	}
}
