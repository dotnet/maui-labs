using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Demonstrates PushModalAsync with native GTK4 dialog windows (default),
/// custom sizing, content-sized dialogs, and the inline fallback.
/// </summary>
class ModalDemoPage : ContentPage
{
	private readonly Label _statusLabel;

	public ModalDemoPage()
	{
		Title = "Modal Pages";

		_statusLabel = new Label
		{
			Text = "Tap a button to push a modal page.",
			HorizontalOptions = LayoutOptions.Center,
			Margin = new Thickness(0, 0, 0, 20),
		};

		// --- Default (full-size dialog) ---
		var nativeModalBtn = new Button
		{
			Text = "Dialog Modal (Default)",
			AutomationId = "btnNativeModal",
			Margin = new Thickness(0, 4),
		};
		nativeModalBtn.Clicked += async (s, e) =>
		{
			var modal = CreateModalContent("Default Dialog",
				"Full-size native GTK4 modal window.\n"
				+ "Matches the parent window dimensions.");
			await Navigation.PushModalAsync(modal);
			_statusLabel.Text = "Default dialog was dismissed.";
		};

		// --- Custom size (400×300) ---
		var customSizeBtn = new Button
		{
			Text = "Dialog Modal (400×300)",
			AutomationId = "btnCustomSize",
			Margin = new Thickness(0, 4),
		};
		customSizeBtn.Clicked += async (s, e) =>
		{
			var modal = CreateModalContent("Small Dialog",
				"Custom sized dialog (400×300).\nResizable with min constraints.");
			GtkPage.SetModalWidth(modal, 400);
			GtkPage.SetModalHeight(modal, 300);
			GtkPage.SetModalMinWidth(modal, 300);
			GtkPage.SetModalMinHeight(modal, 200);
			await Navigation.PushModalAsync(modal);
			_statusLabel.Text = "Custom size dialog was dismissed.";
		};

		// --- Sizes to content ---
		var contentSizeBtn = new Button
		{
			Text = "Dialog Modal (Sizes to Content)",
			AutomationId = "btnContentSize",
			Margin = new Thickness(0, 4),
		};
		contentSizeBtn.Clicked += async (s, e) =>
		{
			var modal = CreateModalContent("Content-Sized",
				"This dialog measured its content\nand sized itself to fit.");
			GtkPage.SetModalSizesToContent(modal, true);
			GtkPage.SetModalMinWidth(modal, 250);
			GtkPage.SetModalMinHeight(modal, 150);
			await Navigation.PushModalAsync(modal);
			_statusLabel.Text = "Content-sized dialog was dismissed.";
		};

		// --- Inline (legacy) ---
		var inlineModalBtn = new Button
		{
			Text = "Inline Modal (Legacy)",
			AutomationId = "btnInlineModal",
			Margin = new Thickness(0, 4),
		};
		inlineModalBtn.Clicked += async (s, e) =>
		{
			var modal = CreateModalContent("Inline Modal",
				"Inline presentation style.\n"
				+ "Hides current content and shows\nthe modal in its place.");
			GtkPage.SetModalPresentationStyle(modal, GtkModalPresentationStyle.Inline);
			await Navigation.PushModalAsync(modal);
			_statusLabel.Text = "Inline modal was dismissed.";
		};

		// --- Stacked ---
		var stackedModalBtn = new Button
		{
			Text = "Push Two Stacked Modals",
			AutomationId = "btnStackedModal",
			Margin = new Thickness(0, 4),
		};
		stackedModalBtn.Clicked += async (s, e) =>
		{
			var first = CreateModalContent("First Modal",
				"This is the first modal dialog.\n"
				+ "Tap 'Push Another' to stack a second modal.");

			var pushAnotherBtn = new Button
			{
				Text = "Push Another Modal",
				AutomationId = "btnPushAnother",
				BackgroundColor = Colors.DarkSlateBlue,
				TextColor = Colors.White,
				Margin = new Thickness(0, 8),
			};
			pushAnotherBtn.Clicked += async (s2, e2) =>
			{
				var second = CreateModalContent("Second Modal",
					"Stacked modal dialog.\nDismiss to return to the first.");
				GtkPage.SetModalWidth(second, 400);
				GtkPage.SetModalHeight(second, 300);
				await Navigation.PushModalAsync(second);
			};

			if (first.Content is VerticalStackLayout vsl)
				vsl.Children.Insert(vsl.Children.Count - 1, pushAnotherBtn);

			await Navigation.PushModalAsync(first);
			_statusLabel.Text = "Stacked modals were dismissed.";
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(20),
				Spacing = 8,
				Children =
				{
					new Label
					{
						Text = "Modal Pages Demo",
						FontSize = 22,
						FontAttributes = FontAttributes.Bold,
						Margin = new Thickness(0, 0, 0, 8),
					},
					_statusLabel,

					new Label { Text = "Dialog Presentations", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.CornflowerBlue },
					nativeModalBtn,
					customSizeBtn,
					contentSizeBtn,

					new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

					new Label { Text = "Other Styles", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.CornflowerBlue },
					inlineModalBtn,
					stackedModalBtn,

					new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 },

					new Label
					{
						Text = "• Default opens a full-size native GTK4 dialog\n"
							+ "• Custom size sets explicit dialog dimensions\n"
							+ "• Content-sized measures the MAUI content\n"
							+ "• Inline uses the legacy in-window overlay",
						FontSize = 12,
						TextColor = Colors.Gray,
					},
				}
			}
		};
	}

	private static ContentPage CreateModalContent(string title, string description)
	{
		var dismissBtn = new Button
		{
			Text = "Dismiss",
			AutomationId = "btnDismissModal",
			BackgroundColor = Colors.Tomato,
			TextColor = Colors.White,
			Margin = new Thickness(0, 16),
		};

		var page = new ContentPage
		{
			Title = title,
			BackgroundColor = Color.FromArgb("#F5F5F5"),
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(30),
				Spacing = 12,
				VerticalOptions = LayoutOptions.Center,
				Children =
				{
					new Label
					{
						Text = title,
						FontSize = 24,
						FontAttributes = FontAttributes.Bold,
						HorizontalOptions = LayoutOptions.Center,
					},
					new Label
					{
						Text = description,
						FontSize = 14,
						HorizontalOptions = LayoutOptions.Center,
						HorizontalTextAlignment = TextAlignment.Center,
					},
					dismissBtn,
				}
			}
		};

		dismissBtn.Clicked += async (s, e) =>
		{
			await page.Navigation.PopModalAsync();
		};

		return page;
	}
}
