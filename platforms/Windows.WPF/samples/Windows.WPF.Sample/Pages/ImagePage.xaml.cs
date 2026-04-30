using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.Pages;

public partial class ImagePage : ContentPage
{
	public ImagePage()
	{
		InitializeComponent();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		AnimatedGifImage.IsAnimationPlaying = AnimationSwitch.IsToggled;

		var imagePath = Path.Combine(AppContext.BaseDirectory, "dotnet_bot.png");
		if (!File.Exists(imagePath))
		{
			StreamSourceImage.IsVisible = false;
			return;
		}

		StreamSourceImage.Source = ImageSource.FromStream(() => File.OpenRead(imagePath));
		StreamSourceImage.IsVisible = true;
	}

	void AnimationStartStop_Clicked(object? sender, EventArgs e)
	{
		AnimationSwitch.IsToggled = !AnimationSwitch.IsToggled;
	}

	void AnimationSwitch_Toggled(object? sender, ToggledEventArgs e)
	{
		AnimatedGifImage.IsAnimationPlaying = e.Value;
	}

	void UseUriSource_Clicked(object? sender, EventArgs e)
	{
		var imagePath = Path.Combine(AppContext.BaseDirectory, "animated_heart.gif");
		if (!File.Exists(imagePath))
		{
			return;
		}

		AnimatedGifImage.Source = ImageSource.FromUri(new Uri(imagePath));
		AnimationSwitch.IsToggled = true;
	}
}
