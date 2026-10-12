using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

internal sealed class ShellSectionNavigationObserver : IDisposable
{
	readonly ShellSection _section;
	readonly Action<Page?> _displayPage;

	public ShellSectionNavigationObserver(ShellSection section, Action<Page?> displayPage)
	{
		_section = section;
		_displayPage = displayPage;
		((IShellSectionController)section).NavigationRequested += OnNavigationRequested;
		section.PropertyChanged += OnPropertyChanged;
	}

	public void Refresh() => _displayPage(GetCurrentPage());

	Page? GetCurrentPage() => _section.Stack.Count > 1
		? _section.Stack[^1]
		: GetRootPage();

	Page? GetRootPage() => (_section.CurrentItem as IShellContentController)?.GetOrCreateContent();

	void OnNavigationRequested(object? sender, NavigationRequestedEventArgs args)
	{
		// MAUI 10 raises PopToRoot before replacing the section's stack.
		_displayPage(args.RequestType == NavigationRequestType.PopToRoot ? GetRootPage() : GetCurrentPage());
	}

	void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (args.PropertyName == nameof(ShellSection.CurrentItem))
			Refresh();
	}

	public void Dispose()
	{
		((IShellSectionController)_section).NavigationRequested -= OnNavigationRequested;
		_section.PropertyChanged -= OnPropertyChanged;
	}
}
