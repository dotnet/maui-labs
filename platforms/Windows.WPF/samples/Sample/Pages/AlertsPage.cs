using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class AlertsPage : ContentPage
{
	static Page ConnectedPage => Application.Current!.Windows[0].Page!;

	public AlertsPage()
	{
		Title = "Alerts & Dialogs";

		var resultLabel = new Label
		{
			Text = "Result: (none yet)",
			FontSize = 14,
			TextColor = Colors.DodgerBlue,
		};

		var simpleAlertBtn = new Button { Text = "Simple Alert (OK)" };
		simpleAlertBtn.Clicked += async (s, e) =>
		{
			resultLabel.Text = "Result: Alert requested...";
			await ConnectedPage.DisplayAlertAsync("Hello!", "This is a simple alert with one button.", "OK");
			resultLabel.Text = "Result: Simple alert dismissed";
		};

		var confirmAlertBtn = new Button { Text = "Confirm Alert (Yes / No)" };
		confirmAlertBtn.Clicked += async (s, e) =>
		{
			bool answer = await ConnectedPage.DisplayAlertAsync("Confirm", "Do you want to proceed?", "Yes", "No");
			resultLabel.Text = $"Result: Confirmed = {answer}";
		};

		var actionSheetBtn = new Button { Text = "Action Sheet" };
		actionSheetBtn.Clicked += async (s, e) =>
		{
			string action = await ConnectedPage.DisplayActionSheetAsync(
				"Choose an action",
				"Cancel",
				"Delete",
				"Copy", "Move", "Rename");
			resultLabel.Text = $"Result: Action = {action}";
		};

		var actionSheet2Btn = new Button { Text = "Action Sheet (no destructive)" };
		actionSheet2Btn.Clicked += async (s, e) =>
		{
			string action = await ConnectedPage.DisplayActionSheetAsync(
				"Pick a color",
				"Cancel",
				null,
				"Red", "Green", "Blue", "Yellow");
			resultLabel.Text = $"Result: Color = {action}";
		};

		var promptBtn = new Button { Text = "Text Prompt" };
		promptBtn.Clicked += async (s, e) =>
		{
			string name = await ConnectedPage.DisplayPromptAsync("Your Name", "What should we call you?", placeholder: "Enter name...");
			resultLabel.Text = name != null ? $"Result: Name = {name}" : "Result: Prompt cancelled";
		};

		var promptInitBtn = new Button { Text = "Prompt (with initial value)" };
		promptInitBtn.Clicked += async (s, e) =>
		{
			string value = await ConnectedPage.DisplayPromptAsync("Edit Value", "Modify the text below:", initialValue: "Hello World");
			resultLabel.Text = value != null ? $"Result: Edited = {value}" : "Result: Prompt cancelled";
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 12,
				Padding = new Thickness(24),
				Children =
				{
					new Border
					{
						Stroke = Colors.DodgerBlue,
						StrokeThickness = 1,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
						Padding = new Thickness(16),
						BackgroundColor = Colors.DodgerBlue.WithAlpha(0.05f),
						Content = resultLabel,
					},

					SectionHeader("DisplayAlert"),
					simpleAlertBtn,
					confirmAlertBtn,

					Separator(),

					SectionHeader("DisplayActionSheet"),
					actionSheetBtn,
					actionSheet2Btn,

					Separator(),

					SectionHeader("DisplayPromptAsync"),
					promptBtn,
					promptInitBtn,
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
