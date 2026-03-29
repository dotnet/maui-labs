using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class TabbedPageHandler : GtkViewHandler<ITabbedView, Gtk.Notebook>
{
	public static new IPropertyMapper<ITabbedView, TabbedPageHandler> Mapper =
		new PropertyMapper<ITabbedView, TabbedPageHandler>(ViewMapper)
		{
		};

	public static CommandMapper<ITabbedView, TabbedPageHandler> CommandMapper =
		new(ViewCommandMapper)
		{
		};

	public TabbedPageHandler() : base(Mapper, CommandMapper)
	{
	}

	protected override Gtk.Notebook CreatePlatformView()
	{
		var notebook = Gtk.Notebook.New();
		notebook.SetVexpand(true);
		notebook.SetHexpand(true);
		return notebook;
	}

	protected override void ConnectHandler(Gtk.Notebook platformView)
	{
		base.ConnectHandler(platformView);
		PopulateTabs();

		if (VirtualView is TabbedPage tabbedPage)
		{
			tabbedPage.PagesChanged += OnPagesChanged;
		}

		platformView.OnSwitchPage += OnNotebookPageSwitched;
	}

	protected override void DisconnectHandler(Gtk.Notebook platformView)
	{
		if (VirtualView is TabbedPage tabbedPage)
		{
			tabbedPage.PagesChanged += OnPagesChanged;
		}

		platformView.OnSwitchPage -= OnNotebookPageSwitched;
		base.DisconnectHandler(platformView);
	}

	void OnPagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		PopulateTabs();
	}

	void OnNotebookPageSwitched(Gtk.Notebook sender, Gtk.Notebook.SwitchPageSignalArgs args)
	{
		if (VirtualView is TabbedPage tabbedPage)
		{
			var idx = (int)args.PageNum;
			if (idx >= 0 && idx < tabbedPage.Children.Count)
			{
				tabbedPage.CurrentPage = tabbedPage.Children[idx] as Page;
			}
		}
	}

	void PopulateTabs()
	{
		if (MauiContext == null || PlatformView == null) return;

		// Remove existing pages
		while (PlatformView.GetNPages() > 0)
			PlatformView.RemovePage(0);

		if (VirtualView is not TabbedPage tabbedPage) return;

		foreach (var child in tabbedPage.Children)
		{
			if (child is Page page)
			{
				var platformPage = (Gtk.Widget)page.ToPlatform(MauiContext);
				var tabLabel = Gtk.Label.New(page.Title ?? "Tab");
				PlatformView.AppendPage(platformPage, tabLabel);
			}
		}
	}
}
