using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// Root container for window content. Manages page display within a GTK window.
/// Supports an optional menu bar above the page content.
/// </summary>
public class WindowRootViewContainer : Gtk.Box
{
	private Gtk.Widget? _currentPage;
	private Gtk.Widget? _menuBar;
	private readonly Stack<Gtk.Widget> _modalStack = new();

	public WindowRootViewContainer() : base()
	{
		SetOrientation(Gtk.Orientation.Vertical);
		SetVexpand(true);
		SetHexpand(true);
	}

	public void SetMenuBar(Gtk.Widget? menuBar)
	{
		if (_menuBar != null)
			Remove(_menuBar);

		_menuBar = menuBar;
		if (menuBar != null)
			Prepend(menuBar);
	}

	public void ClearMenuBar()
	{
		SetMenuBar(null);
	}

	public void AddPage(Gtk.Widget page)
	{
		if (_currentPage != null && _modalStack.Count == 0)
		{
			Remove(_currentPage);
		}

		_currentPage = page;

		if (_modalStack.Count == 0)
			Append(page);
	}

	public void RemovePage(Gtk.Widget page)
	{
		if (_currentPage == page)
		{
			Remove(page);
			_currentPage = null;
		}
	}

	/// <summary>
	/// Push a modal page on top of the current content.
	/// Hides the current top (main page or previous modal) and shows the new modal.
	/// </summary>
	public void PushModal(Gtk.Widget page)
	{
		var currentTop = _modalStack.Count > 0 ? _modalStack.Peek() : _currentPage;
		currentTop?.SetVisible(false);

		_modalStack.Push(page);
		page.SetVexpand(true);
		page.SetHexpand(true);
		Append(page);
	}

	/// <summary>
	/// Pop the top modal page and restore the previous content.
	/// </summary>
	public Gtk.Widget? PopModal()
	{
		if (_modalStack.Count == 0)
			return null;

		var modal = _modalStack.Pop();
		Remove(modal);

		var previousTop = _modalStack.Count > 0 ? _modalStack.Peek() : _currentPage;
		previousTop?.SetVisible(true);

		return modal;
	}
}
