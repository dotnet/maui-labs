using System;
using Comet;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class ThemePageState
	{
		public string ThemeInfo { get; set; } = "";
		public bool IsDark { get; set; }
		public string CometThemeName { get; set; } = "";
	}

	public class ThemePage : Component<ThemePageState>
	{
		public ThemePage()
		{
			UpdateThemeInfo();
		}

		public override View Render()
		{
			var theme = ThemeManager.Current();
			var primary = ColorTokens.Primary.Resolve(theme);
			var surface = ColorTokens.Surface.Resolve(theme);
			var onSurface = ColorTokens.OnSurface.Resolve(theme);

			return ScrollView(Orientation.Vertical,
				VStack(16,
					// Title
					Text("App Theme & Comet Style System")
						.FontSize(24)
						.FontWeight(FontWeight.Bold)
						.HorizontalTextAlignment(TextAlignment.Center),

					// MAUI theme info
					Text(State.ThemeInfo)
						.FontSize(14)
						.HorizontalTextAlignment(TextAlignment.Center),

					// Themed elements
					Text("I respond to theme changes")
						.FontSize(18)
						.FontWeight(FontWeight.Bold)
						.HorizontalTextAlignment(TextAlignment.Center)
						.Color(State.IsDark ? Colors.White : Colors.Black)
						.Background(State.IsDark ? Color.FromArgb("#333333") : Color.FromArgb("#E8E8E8"))
						.Padding(new Thickness(20, 12)),

					Border(new Spacer())
						.Frame(width: 200, height: 60)
						.CornerRadius(8)
						.Background(State.IsDark ? Colors.DarkOrange : Colors.CornflowerBlue)
						.HorizontalLayoutAlignment(LayoutAlignment.Center),

					// MAUI theme buttons
					Text("MAUI App Theme:")
						.FontSize(14).FontWeight(FontWeight.Semibold),
					GalleryPageHelpers.ButtonRow(8,
						Button("Force Light", () =>
						{
							if (Microsoft.Maui.Controls.Application.Current != null)
								Microsoft.Maui.Controls.Application.Current.UserAppTheme = MauiAppTheme.Light;
							UpdateThemeInfo();
						}),
						Button("Force Dark", () =>
						{
							if (Microsoft.Maui.Controls.Application.Current != null)
								Microsoft.Maui.Controls.Application.Current.UserAppTheme = MauiAppTheme.Dark;
							UpdateThemeInfo();
						}),
						Button("Follow System", () =>
						{
							if (Microsoft.Maui.Controls.Application.Current != null)
								Microsoft.Maui.Controls.Application.Current.UserAppTheme = MauiAppTheme.Unspecified;
							UpdateThemeInfo();
						})
					),

					// Comet theme section
					new ShapeView(new Rectangle())
						.Background(Colors.Grey).Frame(height: 1).Opacity(0.3f),

					Text("Comet Design Token Theme:")
						.FontSize(14).FontWeight(FontWeight.Semibold),
					Text($"Active: {State.CometThemeName}")
						.FontSize(13).Color(Colors.DimGrey),

					// Token swatches
					HStack(8,
						TokenSwatch("Primary", primary),
						TokenSwatch("Surface", surface),
						TokenSwatch("OnSurface", onSurface)
					),

					HStack(8,
						Button("Light Theme", () =>
						{
							var t = Defaults.Light.SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Text);
							ThemeManager.SetTheme(t);
							SetState(s => s.CometThemeName = "Light");
						}),
						Button("Dark Theme", () =>
						{
							var t = Defaults.Dark.SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Text);
							ThemeManager.SetTheme(t);
							SetState(s => s.CometThemeName = "Dark");
						})
					).HorizontalLayoutAlignment(LayoutAlignment.Center)
				)
				.VerticalLayoutAlignment(LayoutAlignment.Center)
			)
			.Title("Theme");
		}

		void UpdateThemeInfo()
		{
			var app = Microsoft.Maui.Controls.Application.Current;
			var cometTheme = ThemeManager.Current();
			if (app == null)
			{
				SetState(s =>
				{
					s.ThemeInfo = "Application not available";
					s.IsDark = false;
					s.CometThemeName = cometTheme.Name ?? "(unnamed)";
				});
				return;
			}

			var platformTheme = app.PlatformAppTheme;
			var userTheme = app.UserAppTheme;
			var effectiveTheme = app.RequestedTheme;

			SetState(s =>
			{
				s.ThemeInfo = $"Platform: {platformTheme} | User: {userTheme} | Effective: {effectiveTheme}";
				s.IsDark = effectiveTheme == MauiAppTheme.Dark;
				s.CometThemeName = cometTheme.Name ?? "(unnamed)";
			});
		}

		static View TokenSwatch(string label, Color bg) =>
			Border(
				Text(label)
					.FontSize(10)
					.FontWeight(FontWeight.Bold)
					.Color(bg.GetLuminosity() > 0.5f ? Colors.Black : Colors.White)
					.HorizontalTextAlignment(TextAlignment.Center)
			)
			.Background(bg)
			.CornerRadius(8)
			.Frame(width: 90, height: 40)
			.Padding(new Thickness(4, 2));
	}
}
