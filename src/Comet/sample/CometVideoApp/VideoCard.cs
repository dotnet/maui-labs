namespace CometVideoApp;

/// <summary>
/// A single full-screen video card view — colored placeholder background simulating video,
/// with creator info at bottom-left. Used when building cards with Comet's view system.
/// Currently unused as VideoFeedPage uses MauiViewHost for reliable rendering.
/// </summary>
public class VideoCard : View
{
	readonly VideoModel video;

	public VideoCard(VideoModel video)
	{
		this.video = video;
	}

	[Body]
	View body() => new Grid
	{
		new BoxView(Color.FromArgb(video.ThumbnailColor))
			.FillHorizontal()
			.FillVertical(),

		Text(">")
			.FontSize(72)
			.Color(new Color(255, 255, 255, 60))
			.HorizontalTextAlignment(TextAlignment.Center)
			.Alignment(Alignment.Center),

		VStack(4,
			Text(video.Creator)
				.FontSize(16)
				.FontWeight(FontWeight.Bold)
				.Color(Colors.White),
			Text(video.Description)
				.FontSize(14)
				.Color(new Color(255, 255, 255, 200))
		)
		.Margin(new Thickness(16, 0, 80, 80))
		.Alignment(Alignment.BottomLeading),
	};
}
