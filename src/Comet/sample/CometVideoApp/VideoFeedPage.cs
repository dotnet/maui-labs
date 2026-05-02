using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;
using Microsoft.Maui.Controls;

namespace CometVideoApp;

/// <summary>
/// Full-screen immersive video feed matching the MauiReactor VideoApp layout.
/// Shows one full-screen video at a time with colored placeholder background,
/// creator info at bottom-left, and floating reaction emojis.
/// Uses MauiViewHost pattern (proven in CometWeather) for reliable dark rendering.
/// </summary>
public class VideoFeedPage : Comet.View
{
	[Body]
	Comet.View body()
	{
		var video = VideoModel.All[0];

		var root = new MauiGrid
		{
			BackgroundColor = Color.FromArgb(video.ThumbnailColor),
		};

		// Full-screen colored background (simulating video)
		root.Add(new MauiBoxView
		{
			Color = Color.FromArgb(video.ThumbnailColor),
		});

		// Subtle centered play icon
		root.Add(new MauiLabel
		{
			Text = ">",
			FontSize = 72,
			TextColor = new Color(255, 255, 255, 60),
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			HorizontalTextAlignment = TextAlignment.Center,
		});

		// Bottom-left creator info overlay
		var infoStack = new Microsoft.Maui.Controls.VerticalStackLayout
		{
			Spacing = 4,
			Padding = new Thickness(16, 0, 80, 90),
			VerticalOptions = LayoutOptions.End,
			HorizontalOptions = LayoutOptions.Start,
		};
		infoStack.Add(new MauiLabel
		{
			Text = video.Creator,
			FontSize = 16,
			FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
			TextColor = Colors.White,
		});
		infoStack.Add(new MauiLabel
		{
			Text = video.Description,
			FontSize = 14,
			TextColor = new Color(255, 255, 255, 200),
		});
		root.Add(infoStack);

		// Floating reaction emojis (matching MauiReactor FeedbackFlow)
		AddReactionEmojis(root);

		return new MauiViewHost(root);
	}

	static void AddReactionEmojis(MauiGrid root)
	{
		// Position emojis clustered in bottom-center area matching MauiReactor FeedbackFlow
		var emojis = new (string emoji, int size, double left, double right, double bottom)[]
		{
			("heart", 32, 80, -1, 260),
			("smile", 28, 180, -1, 200),
			("like", 30, -1, 100, 230),
			("heart", 26, 130, -1, 150),
			("smile", 24, -1, 130, 170),
			("heart", 34, -1, 70, 130),
		};

		foreach (var (emoji, size, left, right, bottom) in emojis)
		{
			var label = new MauiLabel
			{
				Text = emoji,
				FontSize = size,
				VerticalOptions = LayoutOptions.End,
				HorizontalOptions = left >= 0 ? LayoutOptions.Start : LayoutOptions.End,
				Margin = new Thickness(
					left >= 0 ? left : 0,
					0,
					right >= 0 ? right : 0,
					bottom),
			};
			root.Add(label);
		}
	}
}
