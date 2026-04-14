using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class RadioButtonPage : ContentPage
{
	public RadioButtonPage()
	{
		Title = "RadioButton";

		var selectedLabel = new Label
		{
			Text = "Selected: None",
			FontSize = 16,
			FontAttributes = FontAttributes.Bold,
			TextColor = Colors.DodgerBlue,
			Margin = new Thickness(0, 0, 0, 10),
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = 20,
				Spacing = 16,
				Children =
				{
					new Label { Text = "RadioButton Demos", FontSize = 24, FontAttributes = FontAttributes.Bold },
					selectedLabel,

					SectionHeader("Native RadioButton (NSButton)"),
					new Label { Text = "Standard native macOS radio buttons:", FontSize = 13, TextColor = Colors.Gray },
					BuildNativeGroup("size", new[] { "Small", "Medium", "Large" }, selectedLabel),

					SectionHeader("Color Selection"),
					new Label { Text = "Choose a color:", FontSize = 13, TextColor = Colors.Gray },
					BuildNativeGroup("color", new[] { "Red", "Green", "Blue", "Yellow" }, selectedLabel),

					SectionHeader("Plan Selection"),
					new Label { Text = "Select a plan:", FontSize = 13, TextColor = Colors.Gray },
					BuildNativeGroup("plan", new[] { "Free", "Pro", "Enterprise" }, selectedLabel),
				}
			}
		};
	}

	static View BuildNativeGroup(string groupName, string[] options, Label selectedLabel)
	{
		var group = new VerticalStackLayout { Spacing = 4, Margin = new Thickness(8, 0) };
		foreach (var option in options)
		{
			var rb = new RadioButton
			{
				Content = option,
				GroupName = groupName,
				IsChecked = option == options[0],
			};
			rb.CheckedChanged += (s, e) =>
			{
				if (e.Value) selectedLabel.Text = $"Selected: {option}";
			};
			group.Add(rb);
		}
		return group;
	}

	static Label SectionHeader(string title) => new Label
	{
		Text = title,
		FontSize = 18,
		FontAttributes = FontAttributes.Bold,
		Margin = new Thickness(0, 12, 0, 4),
	};
}
