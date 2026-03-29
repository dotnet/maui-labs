using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class WindowHandler : ElementHandler<IWindow, Gtk.Window>
{
	public static IPropertyMapper<IWindow, WindowHandler> Mapper =
		new PropertyMapper<IWindow, WindowHandler>(ElementHandler.ElementMapper)
		{
			[nameof(IWindow.Title)] = MapTitle,
			[nameof(IWindow.Content)] = MapContent,
			[nameof(IWindow.Width)] = MapWidth,
			[nameof(IWindow.Height)] = MapHeight,
			[nameof(IWindow.X)] = MapX,
			[nameof(IWindow.Y)] = MapY,
		};

	public static CommandMapper<IWindow, WindowHandler> CommandMapper = new(ElementCommandMapper)
	{
	};

	private readonly Dictionary<Page, Gtk.Window> _modalDialogs = new();

	public WindowHandler() : base(Mapper, CommandMapper)
	{
	}

	public WindowHandler(IPropertyMapper? mapper) : base(mapper ?? Mapper, CommandMapper)
	{
	}

	protected override Gtk.Window CreatePlatformElement()
	{
		return MauiContext?.Services.GetService(typeof(Gtk.Window)) as Gtk.Window
			?? new Gtk.Window();
	}

	protected override void ConnectHandler(Gtk.Window platformView)
	{
		base.ConnectHandler(platformView);

		if (platformView.GetChild() == null)
		{
			platformView.SetChild(new WindowRootViewContainer());
		}

		platformView.OnCloseRequest += OnCloseRequest;
		platformView.OnNotify += OnWindowNotify;

		if (VirtualView is Microsoft.Maui.Controls.Window mauiWindow)
		{
			mauiWindow.ModalPushed += OnModalPushed;
			mauiWindow.ModalPopped += OnModalPopped;
		}
	}

	protected override void DisconnectHandler(Gtk.Window platformView)
	{
		if (VirtualView is Microsoft.Maui.Controls.Window mauiWindow)
		{
			mauiWindow.ModalPushed -= OnModalPushed;
			mauiWindow.ModalPopped -= OnModalPopped;
		}

		foreach (var dialog in _modalDialogs.Values)
		{
			dialog.SetChild(null);
			dialog.Close();
		}
		_modalDialogs.Clear();

		platformView.OnCloseRequest -= OnCloseRequest;
		platformView.OnNotify -= OnWindowNotify;
		base.DisconnectHandler(platformView);
	}

	private bool OnCloseRequest(Gtk.Window sender, EventArgs args)
	{
		if (VirtualView != null)
		{
			GtkMauiApplication.Current.UnregisterWindow(VirtualView);
			VirtualView.Destroying();
		}
		return false; // allow GTK to destroy the window
	}

	private void OnWindowNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
	{
		if (VirtualView == null) return;
		var prop = args.Pspec.GetName();

		if (prop == "is-active")
		{
			if (PlatformView?.GetIsActive() == true)
				VirtualView.Activated();
			else
				VirtualView.Deactivated();
		}
	}

	private void OnModalPushed(object? sender, ModalPushedEventArgs e)
	{
		if (MauiContext == null || PlatformView == null) return;

		var style = e.Modal is Page p
			? GtkPage.GetModalPresentationStyle(p)
			: GtkModalPresentationStyle.Dialog;

		if (style == GtkModalPresentationStyle.Inline)
		{
			// Inline: hide current content and show modal within the same window
			var container = PlatformView.GetChild() as WindowRootViewContainer;
			if (container == null) return;

			var platformContent = (Gtk.Widget)e.Modal.ToPlatform(MauiContext);
			container.PushModal(platformContent);
		}
		else
		{
			// Native GTK4 modal dialog window (default)
			var platformContent = (Gtk.Widget)e.Modal.ToPlatform(MauiContext);

			var dialog = new Gtk.Window();
			dialog.SetModal(true);
			dialog.SetTransientFor(PlatformView);
			dialog.SetTitle((e.Modal as Page)?.Title ?? string.Empty);

			var (width, height) = ComputeDialogSize(e.Modal as Page);

			// Use SetSizeRequest on the content widget so that the requested
			// dimensions describe the *content area*.  GTK will size the window
			// to content + CSD titlebar automatically.
			platformContent.SetSizeRequest((int)width, (int)height);

			// Apply minimum size constraints
			if (e.Modal is Page modalPage2)
			{
				var minW = GtkPage.GetModalMinWidth(modalPage2);
				var minH = GtkPage.GetModalMinHeight(modalPage2);
				if (minW > 0 || minH > 0)
					dialog.SetSizeRequest(minW > 0 ? (int)minW : -1, minH > 0 ? (int)minH : -1);
			}

			var app = PlatformView.GetApplication();
			if (app != null)
				dialog.SetApplication(app);

			platformContent.SetVexpand(true);
			platformContent.SetHexpand(true);
			dialog.SetChild(platformContent);

			if (e.Modal is Page modalPage)
			{
				_modalDialogs[modalPage] = dialog;

				dialog.OnCloseRequest += (_, _) =>
				{
					// Remove from tracking first; if already gone this is a
					// programmatic close from OnModalPopped — just allow it.
					if (!_modalDialogs.Remove(modalPage))
						return false;

					// User clicked X — let GTK close the window immediately
					// and tell MAUI to pop the modal.
					if (VirtualView is Microsoft.Maui.Controls.Window mauiWindow)
						_ = mauiWindow.Navigation.PopModalAsync();
					return false;
				};
			}

			dialog.Present();
		}
	}

	private (double width, double height) ComputeDialogSize(Page? page)
	{
		PlatformView!.GetDefaultSize(out var pw, out var ph);
		double parentWidth = pw > 0 ? pw : 800;
		double parentHeight = ph > 0 ? ph : 600;

		if (page == null)
			return (parentWidth, parentHeight);

		var requestedWidth = GtkPage.GetModalWidth(page);
		var requestedHeight = GtkPage.GetModalHeight(page);
		var sizesToContent = GtkPage.GetModalSizesToContent(page);

		double width = parentWidth;
		double height = parentHeight;

		if (requestedWidth > 0)
			width = requestedWidth;

		if (requestedHeight > 0)
			height = requestedHeight;

		// When sizing to content, measure the page's Content (not the Page itself,
		// since Page always fills available space).
		if (sizesToContent && (requestedWidth <= 0 || requestedHeight <= 0))
		{
			var contentView = (page as ContentPage)?.Content as IView;
			if (contentView != null)
			{
				var measured = contentView.Measure(
					double.PositiveInfinity,
					double.PositiveInfinity);

				var padding = page.Padding;
				var contentWidth = measured.Width + padding.Left + padding.Right;
				var contentHeight = measured.Height + padding.Top + padding.Bottom;

				if (requestedWidth <= 0)
					width = contentWidth;
				if (requestedHeight <= 0)
					height = contentHeight;
			}
		}

		// Apply min size constraints
		var minWidth = GtkPage.GetModalMinWidth(page);
		var minHeight = GtkPage.GetModalMinHeight(page);
		if (minWidth > 0 && width < minWidth) width = minWidth;
		if (minHeight > 0 && height < minHeight) height = minHeight;

		// Don't exceed parent window size
		if (width > parentWidth) width = parentWidth;
		if (height > parentHeight) height = parentHeight;

		return (width, height);
	}

	private void OnModalPopped(object? sender, ModalPoppedEventArgs e)
	{
		if (e.Modal is Page page && _modalDialogs.Remove(page, out var dialog))
		{
			// Programmatic pop — close the native dialog window
			dialog.Close();
		}
		else if (e.Modal is not Page mp
			|| GtkPage.GetModalPresentationStyle(mp) == GtkModalPresentationStyle.Inline)
		{
			// Inline modal — pop from the container
			var container = PlatformView?.GetChild() as WindowRootViewContainer;
			container?.PopModal();
		}
		// else: user-initiated dialog close — already closed by GTK via OnCloseRequest
	}

	public static void MapTitle(WindowHandler handler, IWindow window)
	{
		handler.PlatformView?.SetTitle(window.Title ?? string.Empty);
	}

	public static void MapContent(WindowHandler handler, IWindow window)
	{
		_ = handler.MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

		var child = handler.PlatformView?.GetChild();
		if (child is WindowRootViewContainer container && window.Content != null)
		{
			var platformContent = (Gtk.Widget)window.Content.ToPlatform(handler.MauiContext);
			container.AddPage(platformContent);
		}

		// Apply MenuBar from the content page
		if (handler.PlatformView != null && window.Content is Microsoft.Maui.Controls.Page page)
		{
			GtkMenuBarManager.ApplyToWindow(handler.PlatformView, page);
		}

		// Ensure AlertManager.Subscribe() is called so DI-registered
		// IAlertManagerSubscription gets picked up for DisplayAlert etc.
		if (window is Microsoft.Maui.Controls.Window mauiWindow)
		{
			try
			{
				var amProp = typeof(Microsoft.Maui.Controls.Window).GetProperty("AlertManager",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				var alertManager = amProp?.GetValue(mauiWindow);
				var subscribe = alertManager?.GetType().GetMethod("Subscribe",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				subscribe?.Invoke(alertManager, null);
			}
			catch { }
		}
	}

	public static void MapWidth(WindowHandler handler, IWindow window)
	{
		if (handler.PlatformView == null || window.Width < 0) return;
		handler.PlatformView.GetDefaultSize(out _, out var h);
		handler.PlatformView.SetDefaultSize((int)window.Width, h > 0 ? h : 600);
	}

	public static void MapHeight(WindowHandler handler, IWindow window)
	{
		if (handler.PlatformView == null || window.Height < 0) return;
		handler.PlatformView.GetDefaultSize(out var w, out _);
		handler.PlatformView.SetDefaultSize(w > 0 ? w : 800, (int)window.Height);
	}

	public static void MapX(WindowHandler handler, IWindow window)
	{
		// GTK4 on Wayland does not support setting window position.
		// On X11, this would require platform-specific code.
	}

	public static void MapY(WindowHandler handler, IWindow window)
	{
		// GTK4 on Wayland does not support setting window position.
		// On X11, this would require platform-specific code.
	}
}
