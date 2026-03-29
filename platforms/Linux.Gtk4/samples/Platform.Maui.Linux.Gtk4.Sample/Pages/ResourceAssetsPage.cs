using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

public class ResourceAssetsPage : ContentPage
{
	readonly Label _imageStatus;
	readonly Label _fontStatus;
	readonly Label _appIconStatus;

	public ResourceAssetsPage()
	{
		Title = "Resource Image + Font + AppIcon";
		_imageStatus = new Label { FontSize = 12, TextColor = Colors.Gray };
		_fontStatus = new Label { FontSize = 12, TextColor = Colors.Gray };
		_appIconStatus = new Label { FontSize = 12, TextColor = Colors.Gray };

		var refreshButton = new Button { Text = "Refresh Resource Status" };
		refreshButton.Clicked += (s, e) => UpdateStatus();

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(24),
				Spacing = 12,
				Children =
				{
					new Label
					{
						Text = "🖼️ MAUI Resource Demo",
						FontSize = 28,
						FontAttributes = FontAttributes.Bold,
					},
					new Label
					{
						Text = "ImageSource and font alias below come from Resources/* items.",
						FontSize = 14,
						TextColor = Colors.Gray,
					},
					new Label
					{
						Text = "App icon comes from MauiIcon item metadata.",
						FontSize = 14,
						TextColor = Colors.Gray,
					},
					new Border
					{
						Stroke = Colors.LightGray,
						Padding = new Thickness(12),
						Content = new Image
						{
							Source = "dotnet_bot.png",
							HeightRequest = 170,
							Aspect = Aspect.AspectFit,
						},
					},
					_imageStatus,
					new Label { Text = "Default system font preview", FontSize = 20 },
					new Label
					{
						Text = "OpenSansRegular font alias preview",
						FontSize = 20,
						FontFamily = "OpenSansRegular",
					},
					new Label
					{
						Text = "OpenSans-Regular.ttf filename preview",
						FontSize = 20,
						FontFamily = "OpenSans-Regular.ttf",
					},
					new Label
					{
						Text = "OpenSansRegular bold + italic preview",
						FontSize = 20,
						FontFamily = "OpenSansRegular",
						FontAttributes = FontAttributes.Bold | FontAttributes.Italic,
					},
					new Entry
					{
						Text = "Entry control using OpenSansRegular",
						FontFamily = "OpenSansRegular",
					},
					new Editor
					{
						Text = "Editor control using OpenSansRegular.\nTry typing here to verify runtime text controls use the same font mapping.",
						FontFamily = "OpenSansRegular",
						AutoSize = EditorAutoSizeOption.TextChanges,
						HeightRequest = 90,
					},
					_fontStatus,
					_appIconStatus,
					refreshButton,
				}
			}
		};

		UpdateStatus();
	}

	void UpdateStatus()
	{
		var baseDirectory = AppContext.BaseDirectory;
		var imagePath = Path.Combine(baseDirectory, "dotnet_bot.png");
		var fontPath = Path.Combine(baseDirectory, "OpenSans-Regular.ttf");
		var appIconDirectory = Path.Combine(baseDirectory, "hicolor", "scalable", "apps");
		var appIconThemeIndexPath = Path.Combine(baseDirectory, "hicolor", "index.theme");
		var appIconFilePath = Directory.Exists(appIconDirectory)
			? Directory.EnumerateFiles(appIconDirectory)
				.Select(path => new FileInfo(path))
				.OrderByDescending(static info => info.LastWriteTimeUtc)
				.Select(static info => info.FullName)
				.FirstOrDefault()
			: null;
		var appIconName = string.IsNullOrWhiteSpace(appIconFilePath)
			? "<none>"
			: Path.GetFileNameWithoutExtension(appIconFilePath);

		_imageStatus.Text = $"dotnet_bot.png exists: {File.Exists(imagePath)}";
		_fontStatus.Text = $"OpenSans-Regular.ttf exists: {File.Exists(fontPath)}. Compare alias/font-file labels plus Entry/Editor previews above.";
		_appIconStatus.Text = $"App icon ({appIconName}) exists: {(!string.IsNullOrWhiteSpace(appIconFilePath) && File.Exists(appIconFilePath))}, hicolor index.theme exists: {File.Exists(appIconThemeIndexPath)}";
	}
}
