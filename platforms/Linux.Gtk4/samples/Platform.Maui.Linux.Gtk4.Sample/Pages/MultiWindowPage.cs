using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

class MultiWindowPage : ContentPage
{
	static int _windowCount;
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

		var openBtn = new Button
		{
			Text = "Open New Window",
			BackgroundColor = Colors.DodgerBlue,
			TextColor = Colors.White,
		};
		openBtn.Clicked += (s, e) =>
		{
			Application.Current?.OpenWindow(new Window(new SecondaryWindowPage(++_windowCount)));
			Dispatcher.Dispatch(UpdateWindowCount);
		};

		Content = new VerticalStackLayout
		{
			Spacing = 16,
			Padding = new Thickness(24),
			Children =
			{
				new Label
				{
					Text = "🪟 Multi-Window Support",
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
				},
				new Label
				{
					Text = "Open additional windows from your MAUI app. Each window has its own content and lifecycle.",
					TextColor = Colors.Gray,
				},
				_windowCountLabel,
				openBtn,
				new Label
				{
					Text = "Each window is tracked in the Application.Windows collection with full lifecycle support (Created, Activated, Deactivated, Destroying).",
					FontSize = 12,
					TextColor = Colors.DimGray,
				},
			},
		};
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		UpdateWindowCount();
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
		_windowCountLabel.Text = $"Windows: {Application.Current?.Windows?.Count ?? 0}";
	}
}

class SecondaryWindowPage : ContentPage
{
	public SecondaryWindowPage(int number)
	{
		Title = $"Window {number}";

		var closeBtn = new Button
		{
			Text = "Close This Window",
			BackgroundColor = Colors.Crimson,
			TextColor = Colors.White,
		};
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
			Padding = new Thickness(24),
			Children =
			{
				new Label
				{
					Text = $"🪟 Window {number}",
					FontSize = 28,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				new Label
				{
					Text = "This is a secondary window created via\nApplication.OpenWindow().",
					HorizontalTextAlignment = TextAlignment.Center,
					TextColor = Colors.Gray,
				},
				closeBtn,
			},
		};
	}
}
