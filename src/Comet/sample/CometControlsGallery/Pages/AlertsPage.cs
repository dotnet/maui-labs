using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class AlertsPageState
	{
		public string Result { get; set; } = "Result: (none yet)";
	}

	public class AlertsPage : Component<AlertsPageState>
	{
		static Microsoft.Maui.Controls.Page GetCurrentPage()
		{
			if (Microsoft.Maui.Controls.Application.Current?.Windows?.Count > 0)
				return Microsoft.Maui.Controls.Application.Current.Windows[0].Page;
			return null;
		}

		public override View Render() =>
			GalleryPageHelpers.Scaffold("Alerts & Dialogs",
				Border(
					Text(() => State.Result)
						.FontSize(14)
						.Color(Colors.DodgerBlue)
				)
				.StrokeColor(Colors.DodgerBlue)
				.StrokeThickness(1)
				.CornerRadius(8)
				.Background(Colors.DodgerBlue.WithAlpha(0.05f))
				.Padding(new Thickness(16)),

				GalleryPageHelpers.SectionHeader("DisplayAlert"),
				Button("Simple Alert (OK)", ShowSimpleAlert),
				Button("Confirm Alert (Yes / No)", ShowConfirmAlert),

				GalleryPageHelpers.Separator(),

				GalleryPageHelpers.SectionHeader("DisplayActionSheet"),
				Button("Action Sheet", ShowActionSheet),
				Button("Action Sheet (no destructive)", ShowActionSheetNoDestructive),

				GalleryPageHelpers.Separator(),

				GalleryPageHelpers.SectionHeader("DisplayPromptAsync"),
				Button("Text Prompt", ShowTextPrompt),
				Button("Prompt (with initial value)", ShowPromptWithInitialValue)
			);

		async void ShowSimpleAlert()
		{
			var page = GetCurrentPage();
			if (page == null) return;
			SetState(s => s.Result = "Result: Alert requested...");
			await page.DisplayAlertAsync("Hello!", "This is a simple alert with one button.", "OK");
			SetState(s => s.Result = "Result: Simple alert dismissed");
		}

		async void ShowConfirmAlert()
		{
			var page = GetCurrentPage();
			if (page == null) return;
			bool answer = await page.DisplayAlertAsync("Confirm", "Do you want to proceed?", "Yes", "No");
			SetState(s => s.Result = $"Result: Confirmed = {answer}");
		}

		async void ShowActionSheet()
		{
			var page = GetCurrentPage();
			if (page == null) return;
			string action = await page.DisplayActionSheetAsync(
				"Choose an action", "Cancel", "Delete",
				"Copy", "Move", "Rename");
			SetState(s => s.Result = $"Result: Action = {action}");
		}

		async void ShowActionSheetNoDestructive()
		{
			var page = GetCurrentPage();
			if (page == null) return;
			string action = await page.DisplayActionSheetAsync(
				"Pick a color", "Cancel", null,
				"Red", "Green", "Blue", "Yellow");
			SetState(s => s.Result = $"Result: Color = {action}");
		}

		async void ShowTextPrompt()
		{
			var page = GetCurrentPage();
			if (page == null) return;
			string name = await page.DisplayPromptAsync(
				"Your Name", "What should we call you?",
				placeholder: "Enter name...");
			SetState(s => s.Result = name != null
				? $"Result: Name = {name}"
				: "Result: Prompt cancelled");
		}

		async void ShowPromptWithInitialValue()
		{
			var page = GetCurrentPage();
			if (page == null) return;
			string value = await page.DisplayPromptAsync(
				"Edit Value", "Modify the text below:",
				initialValue: "Hello World");
			SetState(s => s.Result = value != null
				? $"Result: Edited = {value}"
				: "Result: Prompt cancelled");
		}
	}
}
