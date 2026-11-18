using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.Pages;

public class ResourceAssetsPage : ContentPage
{
	readonly Label _imageStatus;
	readonly Label _fontStatus;
	readonly Label _appIconStatus;
	readonly Label _assetStatus;
	readonly Label _splashStatus;

	public ResourceAssetsPage()
	{
		Title = "Resource Image + Font + AppIcon";
		_imageStatus = new Label { FontSize = 12, TextColor = Colors.Gray };
		_fontStatus = new Label { FontSize = 12, TextColor = Colors.Gray };
		_appIconStatus = new Label { FontSize = 12, TextColor = Colors.Gray };
		_assetStatus = new Label { FontSize = 12, TextColor = Colors.Gray };
		_splashStatus = new Label { FontSize = 12, TextColor = Colors.Gray };

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
					_assetStatus,
					_splashStatus,
					refreshButton,
				}
			}
		};

		UpdateStatus();
	}

	void UpdateStatus()
	{
		var baseDirectory = AppContext.BaseDirectory;
		var imagePath = Path.Combine(baseDirectory, "Resources", "Images", "dotnet_bot.png");
		var fontPath = Path.Combine(baseDirectory, "Resources", "Fonts", "OpenSans-Regular.ttf");
		var appIconDirectory = Path.Combine(baseDirectory, "Resources", "AppIcon");
		var appIconFilePath = Directory.Exists(appIconDirectory)
			? Directory.EnumerateFiles(appIconDirectory, "appicon.*").FirstOrDefault()
			: null;
		var appIconName = appIconFilePath == null ? "<none>" : Path.GetFileName(appIconFilePath);
		var assetPath = Path.Combine(baseDirectory, "Pipeline", "payload.txt");
		var splashPath = Path.Combine(baseDirectory, "Resources", "Splash", "dotnet_bot.svg");

		_imageStatus.Text = $"dotnet_bot.png exists: {File.Exists(imagePath)}";
		_fontStatus.Text = $"OpenSans-Regular.ttf exists: {File.Exists(fontPath)}. Compare alias/font-file labels plus Entry/Editor previews above.";
		_appIconStatus.Text = $"Processed MauiIcon ({appIconName}) exists in Resources/AppIcon: {(!string.IsNullOrWhiteSpace(appIconFilePath) && File.Exists(appIconFilePath))}";
		_assetStatus.Text = $"MauiAsset Pipeline/payload.txt exists: {File.Exists(assetPath)}";
		_splashStatus.Text = $"MauiSplashScreen source exists in Resources/Splash: {File.Exists(splashPath)}";
	}
}
