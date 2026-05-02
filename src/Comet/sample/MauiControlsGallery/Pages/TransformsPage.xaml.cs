namespace MauiControlsGallery.Pages;

public partial class TransformsPage : ContentPage
{
	public TransformsPage()
	{
		InitializeComponent();
	}

	async void OnTranslateClicked(object? sender, EventArgs e)
	{
		StatusLabel.Text = "TranslateTo...";
		await TargetBox.TranslateToAsync(100, 0, 500, Easing.CubicInOut);
		StatusLabel.Text = "TranslateTo complete";
	}

	async void OnScaleClicked(object? sender, EventArgs e)
	{
		StatusLabel.Text = "ScaleTo...";
		await TargetBox.ScaleToAsync(1.5, 500, Easing.CubicInOut);
		StatusLabel.Text = "ScaleTo complete";
	}

	async void OnRotateClicked(object? sender, EventArgs e)
	{
		StatusLabel.Text = "RotateTo...";
		await TargetBox.RotateToAsync(90, 500, Easing.CubicInOut);
		StatusLabel.Text = "RotateTo complete";
	}

	async void OnFadeClicked(object? sender, EventArgs e)
	{
		StatusLabel.Text = "FadeTo...";
		await TargetBox.FadeToAsync(0.3, 500, Easing.CubicInOut);
		StatusLabel.Text = "FadeTo complete";
	}

	async void OnResetClicked(object? sender, EventArgs e)
	{
		StatusLabel.Text = "Resetting...";
		await Task.WhenAll(
			TargetBox.TranslateToAsync(0, 0, 300),
			TargetBox.ScaleToAsync(1, 300),
			TargetBox.RotateToAsync(0, 300),
			TargetBox.FadeToAsync(1, 300)
		);
		StatusLabel.Text = "Reset complete";
	}

	async void OnAnchorTopLeft(object? sender, EventArgs e)
	{
		AnchorBox.AnchorX = 0;
		AnchorBox.AnchorY = 0;
		AnchorLabel.Text = "Anchor: (0, 0) — top-left";
		AnchorBox.Rotation = 0;
		await AnchorBox.RotateToAsync(360, 800);
	}

	async void OnAnchorCenter(object? sender, EventArgs e)
	{
		AnchorBox.AnchorX = 0.5;
		AnchorBox.AnchorY = 0.5;
		AnchorLabel.Text = "Anchor: (0.5, 0.5) — center";
		AnchorBox.Rotation = 0;
		await AnchorBox.RotateToAsync(360, 800);
	}

	async void OnAnchorBottomRight(object? sender, EventArgs e)
	{
		AnchorBox.AnchorX = 1;
		AnchorBox.AnchorY = 1;
		AnchorLabel.Text = "Anchor: (1, 1) — bottom-right";
		AnchorBox.Rotation = 0;
		await AnchorBox.RotateToAsync(360, 800);
	}

	async void OnCompositeClicked(object? sender, EventArgs e)
	{
		StatusLabel.Text = "Composite animation running...";
		await Task.WhenAll(
			CompositeBox.TranslateToAsync(80, 0, 600),
			CompositeBox.ScaleToAsync(1.5, 600),
			CompositeBox.RotateToAsync(180, 600),
			CompositeBox.FadeToAsync(0.4, 600)
		);
		await Task.WhenAll(
			CompositeBox.TranslateToAsync(0, 0, 600),
			CompositeBox.ScaleToAsync(1, 600),
			CompositeBox.RotateToAsync(0, 600),
			CompositeBox.FadeToAsync(1, 600)
		);
		StatusLabel.Text = "Composite animation complete";
	}
}
