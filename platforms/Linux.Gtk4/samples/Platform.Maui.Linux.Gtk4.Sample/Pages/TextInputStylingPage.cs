using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Demonstrates text input handler properties: CharacterSpacing, ClearButtonVisibility,
/// Keyboard, VerticalTextAlignment, PlaceholderColor, CursorPosition, SelectionLength,
/// MaxLength, HorizontalTextAlignment, and styling on Slider/Switch/ProgressBar.
/// </summary>
public class TextInputStylingPage : ContentPage
{
	public TextInputStylingPage()
	{
		Title = "Text Input Styling";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 12,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Text Input Styling", FontSize = 24, FontAttributes = FontAttributes.Bold },
					new BoxView { HeightRequest = 2, Color = Colors.DodgerBlue },

					// --- Entry with CharacterSpacing and ClearButton ---
					SectionHeader("Entry — CharacterSpacing & ClearButton"),
					new Entry
					{
						Placeholder = "Wide spacing, clear button",
						CharacterSpacing = 4,
						ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
					},
					new Entry
					{
						Placeholder = "Numeric keyboard hint",
						Keyboard = Keyboard.Numeric,
						CharacterSpacing = 2,
					},
					new Entry
					{
						Placeholder = "Email keyboard hint",
						Keyboard = Keyboard.Email,
					},
					new Entry
					{
						Text = "Center-aligned, max 20 chars",
						HorizontalTextAlignment = TextAlignment.Center,
						MaxLength = 20,
					},
					new Entry
					{
						Text = "End-aligned text",
						HorizontalTextAlignment = TextAlignment.End,
						TextColor = Colors.DodgerBlue,
					},

					Separator(),

					// --- Editor with CharacterSpacing, HorizontalTextAlignment, MaxLength ---
					SectionHeader("Editor — Alignment & MaxLength"),
					new Editor
					{
						Text = "Center-aligned editor with character spacing",
						HorizontalTextAlignment = TextAlignment.Center,
						CharacterSpacing = 2,
						HeightRequest = 60,
					},
					new Editor
					{
						Placeholder = "Max 50 characters",
						MaxLength = 50,
						HeightRequest = 60,
					},

					Separator(),

					// --- SearchBar with PlaceholderColor and CharacterSpacing ---
					SectionHeader("SearchBar — PlaceholderColor & Spacing"),
					new SearchBar
					{
						Placeholder = "Custom placeholder color",
						PlaceholderColor = Colors.Coral,
						CharacterSpacing = 1,
					},
					new SearchBar
					{
						Placeholder = "Read-only search bar",
						IsReadOnly = true,
						Text = "Cannot edit this",
					},

					Separator(),

					// --- Label styling ---
					SectionHeader("Label — CharacterSpacing & LineHeight"),
					new Label
					{
						Text = "Wide character spacing (4px)",
						CharacterSpacing = 4,
						FontSize = 16,
					},
					new Label
					{
						Text = "This label has increased line height (1.8). When the text wraps to multiple lines, " +
						       "you should see more space between lines compared to the default.",
						LineHeight = 1.8,
						FontSize = 14,
					},
					new Label
					{
						Text = "Bottom-aligned label",
						VerticalTextAlignment = TextAlignment.End,
						HeightRequest = 50,
						BackgroundColor = Color.FromArgb("#f0f0f0"),
					},

					Separator(),

					// --- Button styling ---
					SectionHeader("Button — CornerRadius & Stroke"),
					new Button
					{
						Text = "Rounded Button",
						CornerRadius = 20,
						BackgroundColor = Colors.DodgerBlue,
						TextColor = Colors.White,
						CharacterSpacing = 2,
					},
					new Button
					{
						Text = "Bordered Button",
						BorderColor = Colors.Coral,
						BorderWidth = 2,
						BackgroundColor = Colors.Transparent,
						TextColor = Colors.Coral,
					},

					Separator(),

					// --- Slider with track/thumb colors ---
					SectionHeader("Slider — Track & Thumb Colors"),
					BuildColoredSlider(),

					Separator(),

					// --- Switch with track/thumb colors ---
					SectionHeader("Switch — Track & Thumb Colors"),
					BuildColoredSwitch(),

					Separator(),

					// --- ProgressBar with color ---
					SectionHeader("ProgressBar — ProgressColor"),
					new ProgressBar { Progress = 0.7, ProgressColor = Colors.MediumSeaGreen },
					new ProgressBar { Progress = 0.4, ProgressColor = Colors.Coral },
				}
			}
		};
	}

	static View BuildColoredSlider()
	{
		var label = new Label { Text = "Value: 50", FontSize = 12, TextColor = Colors.Gray };
		var slider = new Slider(0, 100, 50)
		{
			MinimumTrackColor = Colors.DodgerBlue,
			MaximumTrackColor = Colors.LightGray,
			ThumbColor = Colors.DodgerBlue,
		};
		slider.ValueChanged += (s, e) => label.Text = $"Value: {e.NewValue:F0}";
		return new VerticalStackLayout { Spacing = 4, Children = { slider, label } };
	}

	static View BuildColoredSwitch()
	{
		var label = new Label { Text = "Off", FontSize = 12, TextColor = Colors.Gray };
		var sw = new Switch
		{
			OnColor = Colors.MediumSeaGreen,
			ThumbColor = Colors.White,
		};
		sw.Toggled += (s, e) =>
		{
			label.Text = e.Value ? "On" : "Off";
			label.TextColor = e.Value ? Colors.MediumSeaGreen : Colors.Gray;
		};
		return new HorizontalStackLayout { Spacing = 12, Children = { sw, label } };
	}

	static Label SectionHeader(string text) => new()
	{
		Text = text,
		FontSize = 16,
		FontAttributes = FontAttributes.Bold,
		TextColor = Colors.DarkSlateGray,
	};

	static BoxView Separator() => new() { HeightRequest = 1, Color = Colors.LightGray };
}
