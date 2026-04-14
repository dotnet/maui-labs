using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

class MultiWindowPage : ContentPage
{
	readonly Label _windowCountLabel;

	public MultiWindowPage()
	{
		Title = "Multi-Window";

		_windowCountLabel = new Label
		{
			Text = "Windows: 1",
			FontSize = 16,
			HorizontalTextAlignment = TextAlignment.Center,
		};

		var openBtn = new Button { Text = "Open New Window" };
		openBtn.Clicked += (s, e) =>
		{
			Application.Current?.OpenWindow(new Window(new SecondaryWindowPage()));
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		var openUnifiedBtn = new Button { Text = "Open Unified Window" };
		openUnifiedBtn.Clicked += (s, e) =>
		{
			var win = new Window(new SecondaryWindowPage { Title = "Unified Window" });
#if MACAPP
			MacOSWindow.SetTitlebarStyle(win, MacOSTitlebarStyle.Unified);
			MacOSWindow.SetTitleVisibility(win, MacOSTitleVisibility.Visible);
			MacOSWindow.SetTitlebarTransparent(win, false);
#endif
			Application.Current?.OpenWindow(win);
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		var openCompactBtn = new Button { Text = "Open Unified Compact Window" };
		openCompactBtn.Clicked += (s, e) =>
		{
			var win = new Window(new SecondaryWindowPage { Title = "Unified Compact Window" });
#if MACAPP
			MacOSWindow.SetTitlebarStyle(win, MacOSTitlebarStyle.UnifiedCompact);
			MacOSWindow.SetTitleVisibility(win, MacOSTitleVisibility.Visible);
#endif
			Application.Current?.OpenWindow(win);
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		var openExpandedBtn = new Button { Text = "Open Expanded Window" };
		openExpandedBtn.Clicked += (s, e) =>
		{
			var win = new Window(new SecondaryWindowPage { Title = "Expanded Window" });
#if MACAPP
			MacOSWindow.SetTitlebarStyle(win, MacOSTitlebarStyle.Expanded);
			MacOSWindow.SetTitleVisibility(win, MacOSTitleVisibility.Visible);
			MacOSWindow.SetTitlebarTransparent(win, false);
#endif
			Application.Current?.OpenWindow(win);
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		// Sidebar demo buttons
		var shellNativeBtn = new Button { Text = "Shell — Native Sidebar" };
		shellNativeBtn.Clicked += (s, e) =>
		{
			Application.Current?.OpenWindow(new Window(new DemoShell(useNative: true)));
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		var shellCustomBtn = new Button { Text = "Shell — Custom Sidebar" };
		shellCustomBtn.Clicked += (s, e) =>
		{
			Application.Current?.OpenWindow(new Window(new DemoShell(useNative: false)));
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		var flyoutNativeBtn = new Button { Text = "FlyoutPage — Native Sidebar" };
		flyoutNativeBtn.Clicked += (s, e) =>
		{
			Application.Current?.OpenWindow(new Window(new DemoFlyoutPage(useNative: true)));
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		var flyoutCustomBtn = new Button { Text = "FlyoutPage — Custom Sidebar" };
		flyoutCustomBtn.Clicked += (s, e) =>
		{
			Application.Current?.OpenWindow(new Window(new DemoFlyoutPage(useNative: false)));
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		var closeBtn = new Button { Text = "Close This Window" };
		closeBtn.Clicked += (s, e) =>
		{
			if (Window != null)
				Application.Current?.CloseWindow(Window);
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				VerticalOptions = LayoutOptions.Center,
				HorizontalOptions = LayoutOptions.Center,
				Spacing = 12,
				Padding = new Thickness(20),
				Children =
				{
					new Label
					{
						Text = "Multi-Window Support",
						FontSize = 24,
						FontAttributes = FontAttributes.Bold,
						HorizontalTextAlignment = TextAlignment.Center,
					},
					_windowCountLabel,

					new Label { Text = "Window Styles", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					openBtn,
					openUnifiedBtn,
					openCompactBtn,
					openExpandedBtn,

					new Label { Text = "Sidebar Demos", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					new Label { Text = "Each opens a new window with the specified sidebar style.", FontSize = 13, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center },
					shellNativeBtn,
					shellCustomBtn,
					flyoutNativeBtn,
					flyoutCustomBtn,

					closeBtn,
				}
			}
		};
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		UpdateWindowCount();

		// Update count when this window regains focus (e.g. after closing another window)
		if (Window != null)
			Window.Activated += OnWindowActivated;
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		if (Window != null)
			Window.Activated -= OnWindowActivated;
	}

	void OnWindowActivated(object? sender, EventArgs e) => UpdateWindowCount();

	void UpdateWindowCount()
	{
		var count = Application.Current?.Windows?.Count ?? 0;
		_windowCountLabel.Text = $"Windows: {count}";
	}
}

class SecondaryWindowPage : ContentPage
{
	public SecondaryWindowPage()
	{
		Title = "Secondary Window";

		var closeBtn = new Button { Text = "Close This Window" };
		closeBtn.Clicked += (s, e) =>
		{
			if (Window != null)
				Application.Current?.CloseWindow(Window);
		};

		Content = new VerticalStackLayout
		{
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Spacing = 16,
			Children =
			{
				new Label
				{
					Text = "🪟 Secondary Window",
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "This is a new window opened via Application.OpenWindow().\nClose it with the red button or the button below.",
					HorizontalTextAlignment = TextAlignment.Center,
					MaximumWidthRequest = 400,
				},
				closeBtn,
			}
		};
	}
}

/// <summary>
/// A small Shell with a few sidebar items for demonstrating native vs custom sidebar.
/// </summary>
class DemoShell : Shell
{
	public DemoShell(bool useNative)
	{
		Title = useNative ? "Shell — Native Sidebar" : "Shell — Custom Sidebar";
		FlyoutBehavior = FlyoutBehavior.Locked;
#if MACAPP
		MacOSShell.SetUseNativeSidebar(this, useNative);
#endif

		var general = new FlyoutItem { Title = "General" };
		general.Items.Add(MakeContent("Home", "demohome", "house.fill", () => MakePage("Home", "Welcome to the Shell demo!", "#4A90E2")));
		general.Items.Add(MakeContent("Settings", "demosettings", "gear", () => MakePage("Settings", "Adjust your preferences here.", "#7B68EE")));
		Items.Add(general);

		var more = new FlyoutItem { Title = "More" };
		more.Items.Add(MakeContent("About", "demoabout", "info.circle", () => MakePage("About", "MAUI macOS sidebar demo.", "#2ECC71")));
		more.Items.Add(MakeContent("Profile", "demoprofile", "person.circle", () => MakePage("Profile", "View your profile.", "#F39C12")));
		Items.Add(more);
	}

	static ShellContent MakeContent(string title, string route, string systemImage, Func<ContentPage> factory)
	{
		var content = new ShellContent
		{
			Title = title,
			Route = route,
			ContentTemplate = new DataTemplate(factory),
		};
#if MACAPP
		MacOSShell.SetSystemImage(content, systemImage);
#endif
		return content;
	}

	static ContentPage MakePage(string title, string description, string accent)
	{
		var page = new ContentPage
		{
			Title = title,
			Content = new VerticalStackLayout
			{
				VerticalOptions = LayoutOptions.Center,
				HorizontalOptions = LayoutOptions.Center,
				Spacing = 16,
				Children =
				{
					new Border
					{
						BackgroundColor = Color.FromArgb(accent),
						HeightRequest = 4, WidthRequest = 200,
						HorizontalOptions = LayoutOptions.Center,
						StrokeThickness = 0,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
					},
					new Label { Text = title, FontSize = 28, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
					new Label { Text = description, FontSize = 16, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center },
				}
			}
		};

		// Sidebar toolbar item (appears in sidebar titlebar area)
		var sidebarBtn = new ToolbarItem { Text = "Add", IconImageSource = "plus" };
#if MACAPP
		MacOSToolbarItem.SetPlacement(sidebarBtn, MacOSToolbarItemPlacement.Sidebar);
#endif
		sidebarBtn.Clicked += (s, e) => page.DisplayAlert("Sidebar", "Add button clicked!", "OK");
		page.ToolbarItems.Add(sidebarBtn);

		// Content toolbar item (normal area)
		var contentBtn = new ToolbarItem { Text = "Share", IconImageSource = "square.and.arrow.up" };
		contentBtn.Clicked += (s, e) => page.DisplayAlert("Content", "Share button clicked!", "OK");
		page.ToolbarItems.Add(contentBtn);

		return page;
	}
}

/// <summary>
/// A FlyoutPage for demonstrating native vs custom sidebar.
/// </summary>
class DemoFlyoutPage : FlyoutPage
{
	public DemoFlyoutPage(bool useNative)
	{
		Title = useNative ? "FlyoutPage — Native Sidebar" : "FlyoutPage — Custom Sidebar";
		FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split;

#if MACAPP
		if (useNative)
			MacOSFlyoutPage.SetUseNativeSidebar(this, true);

		// Empty flyout page (required by FlyoutPage API; NativeSidebarFlyoutPageHandler uses sidebar items)
		Flyout = new ContentPage { Title = "Menu" };
		Detail = new NavigationPage(MakeDetailPage("Home", "Welcome to the FlyoutPage demo!", "#4A90E2"));

		// Both native and custom use sidebar items (since NativeSidebarFlyoutPageHandler
		// is registered globally). The difference is UseNativeSidebar controls whether
		// the sidebar gets the inset/vibrancy treatment.
		MacOSFlyoutPage.SetSidebarItems(this, new List<MacOSSidebarItem>
		{
			new MacOSSidebarItem
			{
				Title = "General",
				Children = new List<MacOSSidebarItem>
				{
					new() { Title = "Home", SystemImage = "house.fill", Tag = "home" },
					new() { Title = "Settings", SystemImage = "gear", Tag = "settings" },
				}
			},
			new MacOSSidebarItem
			{
				Title = "More",
				Children = new List<MacOSSidebarItem>
				{
					new() { Title = "About", SystemImage = "info.circle", Tag = "about" },
					new() { Title = "Profile", SystemImage = "person.circle", Tag = "profile" },
				}
			},
		});

		MacOSFlyoutPage.SetSidebarSelectionChanged(this, item =>
		{
			Detail = item.Tag switch
			{
				"home" => new NavigationPage(MakeDetailPage("Home", "Welcome to the FlyoutPage demo!", "#4A90E2")),
				"settings" => new NavigationPage(MakeDetailPage("Settings", "Adjust your preferences.", "#7B68EE")),
				"about" => new NavigationPage(MakeDetailPage("About", "MAUI macOS FlyoutPage sidebar demo.", "#2ECC71")),
				"profile" => new NavigationPage(MakeDetailPage("Profile", "View your profile info.", "#F39C12")),
				_ => Detail,
			};
		});
#else
		Flyout = new ContentPage { Title = "Menu" };
		Detail = new NavigationPage(MakeDetailPage("Home", "Welcome to the FlyoutPage demo!", "#4A90E2"));
#endif
	}

	static ContentPage MakeDetailPage(string title, string description, string accent)
	{
		return new ContentPage
		{
			Title = title,
			Content = new VerticalStackLayout
			{
				VerticalOptions = LayoutOptions.Center,
				HorizontalOptions = LayoutOptions.Center,
				Spacing = 16,
				Children =
				{
					new Border
					{
						BackgroundColor = Color.FromArgb(accent),
						HeightRequest = 4, WidthRequest = 200,
						HorizontalOptions = LayoutOptions.Center,
						StrokeThickness = 0,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
					},
					new Label { Text = title, FontSize = 28, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
					new Label { Text = description, FontSize = 16, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center },
				}
			}
		};
	}
}
