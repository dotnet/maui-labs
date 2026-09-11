using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DocumentExtractionSample.Pages;

/// <summary>A simple modal page that shows a block of monospaced text (used for raw Apple JSON and capability
/// summaries) with a way to copy it to the clipboard.</summary>
public sealed class TextViewerPage : ContentPage
{
	public TextViewerPage(string title, string content)
	{
		Title = title;

		var textLabel = new Label
		{
			Text = content,
			FontFamily = "Courier",
			FontSize = 12,
			LineBreakMode = LineBreakMode.CharacterWrap,
			Padding = new Thickness(12),
		};

		var copyButton = new Button { Text = "Copy" };
		copyButton.Clicked += async (_, _) => await Clipboard.Default.SetTextAsync(content);

		var closeButton = new Button { Text = "Close" };
		closeButton.Clicked += async (_, _) => await Navigation.PopModalAsync();

		var buttonRow = new HorizontalStackLayout
		{
			Spacing = 12,
			Margin = new Thickness(12),
			Children = { copyButton, closeButton },
		};

		var grid = new Grid
		{
			RowDefinitions =
			{
				new RowDefinition(GridLength.Star),
				new RowDefinition(GridLength.Auto),
			},
		};
		grid.Add(new ScrollView { Content = textLabel }, column: 0, row: 0);
		grid.Add(buttonRow, column: 0, row: 1);

		Content = grid;
	}
}
