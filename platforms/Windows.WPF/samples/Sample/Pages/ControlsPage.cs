using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class ControlsPage : ContentPage
{
	public ControlsPage()
	{
		Title = "Controls";

		int clickCount = 0;
		var clickLabel = new Label { Text = "Clicks: 0", FontSize = 14 };
		var progressBar = new ProgressBar { Progress = 0 };

		var button = new Button { Text = "Click me!" };
		button.Clicked += (s, e) =>
		{
			clickCount++;
			clickLabel.Text = $"Clicks: {clickCount}";
			progressBar.Progress = Math.Min(1.0, clickCount / 20.0);
		};

		var entryEcho = new Label { Text = "Echo: ", FontSize = 14, TextColor = Colors.Gray };
		var entry = new Entry { Placeholder = "Type here..." };
		entry.TextChanged += (s, e) => entryEcho.Text = $"Echo: {e.NewTextValue}";

		var sliderLabel = new Label { Text = "Slider: 50", FontSize = 14 };
		var slider = new Slider(0, 100, 50);
		slider.ValueChanged += (s, e) => sliderLabel.Text = $"Slider: {e.NewValue:F0}";

		var switchLabel = new Label { Text = "Off", FontSize = 14 };
		var toggle = new Switch();
		toggle.Toggled += (s, e) => switchLabel.Text = e.Value ? "On" : "Off";

		var checkLabel = new Label { Text = "Unchecked", FontSize = 14 };
		var checkBox = new CheckBox();
		checkBox.CheckedChanged += (s, e) => checkLabel.Text = e.Value ? "Checked ✓" : "Unchecked";

		var stepperLabel = new Label { Text = "Stepper: 0", FontSize = 14 };
		var stepper = new Stepper { Minimum = 0, Maximum = 50, Increment = 5 };
		stepper.ValueChanged += (s, e) => stepperLabel.Text = $"Stepper: {e.NewValue}";

		var radioLabel = new Label { Text = "Selected: Option A", FontSize = 14, TextColor = Colors.DodgerBlue };
		var radio1 = new RadioButton { Content = "Option A", GroupName = "demo", IsChecked = true };
		var radio2 = new RadioButton { Content = "Option B", GroupName = "demo" };
		var radio3 = new RadioButton { Content = "Option C", GroupName = "demo" };
		radio1.CheckedChanged += (s, e) => { if (e.Value) radioLabel.Text = "Selected: Option A"; };
		radio2.CheckedChanged += (s, e) => { if (e.Value) radioLabel.Text = "Selected: Option B"; };
		radio3.CheckedChanged += (s, e) => { if (e.Value) radioLabel.Text = "Selected: Option C"; };

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Padding = new Thickness(24),
				Children =
				{
					SectionHeader("Button & ProgressBar"),
					button,
					clickLabel,
					new Label { Text = "Progress (click 20x to fill):", FontSize = 12, TextColor = Colors.Gray },
					progressBar,

					Separator(),

					SectionHeader("Entry"),
					entry,
					entryEcho,

					Separator(),

					SectionHeader("Editor"),
					new Editor { Placeholder = "Multi-line text editor...", HeightRequest = 80 },

					Separator(),

					SectionHeader("Slider"),
					slider,
					sliderLabel,

					Separator(),

					SectionHeader("Switch"),
					new HorizontalStackLayout
					{
						Spacing = 12,
						Children = { toggle, switchLabel }
					},

					Separator(),

					SectionHeader("CheckBox"),
					new HorizontalStackLayout
					{
						Spacing = 12,
						Children = { checkBox, checkLabel }
					},

					Separator(),

					SectionHeader("Stepper (increment by 5)"),
					stepper,
					stepperLabel,

					Separator(),

					SectionHeader("RadioButton"),
					radio1, radio2, radio3,
					radioLabel,
				}
			}
		};
	}

	static Label SectionHeader(string text) => new()
	{
		Text = text,
		FontSize = 16,
		FontAttributes = FontAttributes.Bold,
		TextColor = Colors.CornflowerBlue,
	};

	static Border Separator() => new() { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 };
}
