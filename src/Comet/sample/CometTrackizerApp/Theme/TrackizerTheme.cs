namespace CometTrackizerApp.Theme;

public static class TrackizerTheme
{
	// Colors
	public static Color Grey100 => Color.FromArgb("#0E0E12");
	public static Color Grey80 => Color.FromArgb("#1C1C23");
	public static Color Grey70 => Color.FromArgb("#353542");
	public static Color Grey60 => Color.FromArgb("#4E4E61");
	public static Color Grey50 => Color.FromArgb("#666680");
	public static Color Grey40 => Color.FromArgb("#83839C");
	public static Color Grey30 => Color.FromArgb("#A2A2B5");
	public static Color Grey20 => Color.FromArgb("#C1C1CD");
	public static Color Grey10 => Color.FromArgb("#E0E0E6");
	public static Color White => Color.FromArgb("#FFFFFF");
	public static Color Primary100 => Color.FromArgb("#AD7BFF");
	public static Color Primary50 => Color.FromArgb("#7722FF");
	public static Color Primary20 => Color.FromArgb("#924EFF");
	public static Color Primary05 => Color.FromArgb("#C9A7FF");
	public static Color Primary0 => Color.FromArgb("#E4D3FF");
	public static Color Accentp100 => Color.FromArgb("#FF7966");
	public static Color Accentp50 => Color.FromArgb("#FFA699");
	public static Color Accentp0 => Color.FromArgb("#FFD2CC");
	public static Color Accents100 => Color.FromArgb("#00FAD9");
	public static Color Accents50 => Color.FromArgb("#7DFFEE");

	// Font sizes
	public static double FontSizeH7 => 40;
	public static double FontSizeH6 => 32;
	public static double FontSizeH5 => 24;
	public static double FontSizeH4 => 20;
	public static double FontSizeH3 => 16;
	public static double FontSizeH2 => 14;
	public static double FontSizeH1 => 12;
	public static double FontSizeBodyLarge => 16;
	public static double FontSizeBodyMedium => 14;
	public static double FontSizeBodySmall => 12;
	public static double FontSizeBodyExtraSmall => 10;

	// Typography helpers
	public static View H1(string? text = null) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeH1);

	public static View H2(string? text = null) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeH2);

	public static View H3(string? text = null) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeH3);

	public static View H4(string? text = null) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeH4);

	public static View H5(string? text = null) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeH5);

	public static View H7(string? text = null) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeH7);

	public static View BodySmall(string? text) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeBodySmall);

	public static View BodyMedium(string? text) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeBodyMedium);

	public static View BodyLarge(string? text) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeBodyLarge);

	public static View BodyExtraSmall(string? text) =>
		Text(text ?? "").FontFamily("InterRegular").FontSize(FontSizeBodyExtraSmall);

	// Button styles
	public static View ThemedButton(string text, Action? onClicked) =>
		Button(text, onClicked ?? (() => { }))
			.FontFamily("InterRegular")
			.FontSize(FontSizeH2)
			.Color(White)
			.CornerRadius(25)
			.Background(White.WithAlpha(0.1f))
			.BorderColor(White.WithAlpha(0.15f))
			.BorderWidth(1)
			.Frame(height: 48);

	public static View PrimaryButton(string text, Action? onClicked) =>
		Button(text, onClicked ?? (() => { }))
			.FontFamily("InterRegular")
			.FontSize(FontSizeH2)
			.Color(White)
			.CornerRadius(25)
			.Background(Accentp100)
			.BorderColor(White.WithAlpha(0.15f))
			.BorderWidth(1)
			.Frame(height: 48);

	public static View PrimaryImageButton(
		string text, Action? onClicked, Color baseColor, Color fontColor, string imageSource) =>
		Border(
			HStack(8,
				Image(imageSource).Frame(width: 16, height: 16),
				Text(text).FontFamily("InterRegular").FontSize(FontSizeH2).Color(fontColor)
			).Alignment(Alignment.Center)
		)
		.Background(baseColor)
		.StrokeColor(White.WithAlpha(0.15f))
		.StrokeThickness(1)
		.ClipShape(new RoundedRectangle(25))
		.Frame(height: 48)
		.OnTap(_ => (onClicked ?? (() => { }))());
}
