using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

class ThemePage : ContentPage
{
	readonly Label _themeLabel;

	public ThemePage()
	{
		Title = "Theme";

		_themeLabel = new Label
		{
			FontSize = 14,
			HorizontalTextAlignment = TextAlignment.Center,
			LineBreakMode = LineBreakMode.NoWrap,
		};
		UpdateThemeLabel();

		// Demo label using AppThemeBinding for background/text colors
		var themedLabel = new Label
		{
			Text = "I respond to theme changes",
			FontSize = 18,
			FontAttributes = FontAttributes.Bold,
			HorizontalTextAlignment = TextAlignment.Center,
			Padding = new Thickness(20, 12),
		};
		themedLabel.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.White);
		themedLabel.SetAppThemeColor(Label.BackgroundColorProperty, Color.FromArgb("#E8E8E8"), Color.FromArgb("#333333"));

		var themedBox = new BoxView
		{
			HeightRequest = 60,
			WidthRequest = 200,
			CornerRadius = 8,
			HorizontalOptions = LayoutOptions.Center,
		};
		themedBox.SetAppThemeColor(BoxView.ColorProperty, Colors.CornflowerBlue, Colors.DarkOrange);

		var lightBtn = new Button { Text = "Force Light" };
		lightBtn.Clicked += (s, e) =>
		{
			if (Application.Current != null)
				Application.Current.UserAppTheme = AppTheme.Light;
			UpdateThemeLabel();
		};

		var darkBtn = new Button { Text = "Force Dark" };
		darkBtn.Clicked += (s, e) =>
		{
			if (Application.Current != null)
				Application.Current.UserAppTheme = AppTheme.Dark;
			UpdateThemeLabel();
		};

		var systemBtn = new Button { Text = "Follow System" };
		systemBtn.Clicked += (s, e) =>
		{
			if (Application.Current != null)
				Application.Current.UserAppTheme = AppTheme.Unspecified;
			UpdateThemeLabel();
		};

		if (Application.Current != null)
			Application.Current.RequestedThemeChanged += (s, e) => UpdateThemeLabel();

		Content = new VerticalStackLayout
		{
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Spacing = 16,
			Children =
			{
				new Label
				{
					Text = "App Theme Support",
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
				},
				_themeLabel,
				themedLabel,
				themedBox,
				new HorizontalStackLayout
				{
					Spacing = 8,
					HorizontalOptions = LayoutOptions.Center,
					Children = { lightBtn, darkBtn, systemBtn },
				},
			}
		};
	}

	void UpdateThemeLabel()
	{
		var app = Application.Current;
		if (app == null) return;
		_themeLabel.Text = $"Platform: {app.PlatformAppTheme} | User: {app.UserAppTheme} | Effective: {app.RequestedTheme}";
	}
}
