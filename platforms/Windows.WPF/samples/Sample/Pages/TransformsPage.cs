using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class TransformsPage : ContentPage
{
	public TransformsPage()
	{
		Title = "Transforms";

		var targetBox = new Border
		{
			BackgroundColor = Colors.DodgerBlue,
			WidthRequest = 100,
			HeightRequest = 100,
			HorizontalOptions = LayoutOptions.Center,
			StrokeThickness = 0,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
		};

		var statusLabel = new Label
		{
			Text = "Use the buttons below to animate the box",
			FontSize = 14,
			TextColor = Colors.Gray,
			HorizontalOptions = LayoutOptions.Center,
		};

		var translateBtn = new Button { Text = "TranslateTo (100, 0)" };
		translateBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "TranslateTo...";
			await targetBox.TranslateTo(100, 0, 500, Easing.CubicInOut);
			statusLabel.Text = "TranslateTo complete";
		};

		var scaleBtn = new Button { Text = "ScaleTo 1.5x" };
		scaleBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "ScaleTo...";
			await targetBox.ScaleTo(1.5, 500, Easing.CubicInOut);
			statusLabel.Text = "ScaleTo complete";
		};

		var rotateBtn = new Button { Text = "RotateTo 90°" };
		rotateBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "RotateTo...";
			await targetBox.RotateTo(90, 500, Easing.CubicInOut);
			statusLabel.Text = "RotateTo complete";
		};

		var fadeBtn = new Button { Text = "FadeTo 0.3" };
		fadeBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "FadeTo...";
			await targetBox.FadeTo(0.3, 500, Easing.CubicInOut);
			statusLabel.Text = "FadeTo complete";
		};

		var resetBtn = new Button { Text = "Reset All", BackgroundColor = Colors.Crimson, TextColor = Colors.White };
		resetBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "Resetting...";
			await Task.WhenAll(
				targetBox.TranslateTo(0, 0, 300),
				targetBox.ScaleTo(1, 300),
				targetBox.RotateTo(0, 300),
				targetBox.FadeTo(1, 300)
			);
			targetBox.AnchorX = 0.5;
			targetBox.AnchorY = 0.5;
			statusLabel.Text = "Reset complete";
		};

		// Anchor demo
		var anchorBox = new Border
		{
			BackgroundColor = Colors.MediumPurple,
			WidthRequest = 80,
			HeightRequest = 80,
			HorizontalOptions = LayoutOptions.Center,
			StrokeThickness = 0,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
		};
		var anchorLabel = new Label { Text = "Anchor: (0.5, 0.5) — center", FontSize = 14, HorizontalOptions = LayoutOptions.Center };

		var anchorTopLeft = new Button { Text = "Anchor (0, 0)" };
		anchorTopLeft.Clicked += async (s, e) =>
		{
			anchorBox.AnchorX = 0;
			anchorBox.AnchorY = 0;
			anchorLabel.Text = "Anchor: (0, 0) — top-left";
			await anchorBox.RotateTo(360, 800);
			anchorBox.Rotation = 0;
		};

		var anchorCenter = new Button { Text = "Anchor (0.5, 0.5)" };
		anchorCenter.Clicked += async (s, e) =>
		{
			anchorBox.AnchorX = 0.5;
			anchorBox.AnchorY = 0.5;
			anchorLabel.Text = "Anchor: (0.5, 0.5) — center";
			await anchorBox.RotateTo(360, 800);
			anchorBox.Rotation = 0;
		};

		var anchorBottomRight = new Button { Text = "Anchor (1, 1)" };
		anchorBottomRight.Clicked += async (s, e) =>
		{
			anchorBox.AnchorX = 1;
			anchorBox.AnchorY = 1;
			anchorLabel.Text = "Anchor: (1, 1) — bottom-right";
			await anchorBox.RotateTo(360, 800);
			anchorBox.Rotation = 0;
		};

		// Composite animation
		var compositeBox = new Border
		{
			BackgroundColor = Colors.Coral,
			WidthRequest = 80,
			HeightRequest = 80,
			HorizontalOptions = LayoutOptions.Center,
			StrokeThickness = 0,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
		};

		var compositeBtn = new Button { Text = "Run Composite Animation" };
		compositeBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "Composite animation running...";
			await Task.WhenAll(
				compositeBox.TranslateTo(80, 0, 600, Easing.CubicInOut),
				compositeBox.ScaleTo(1.5, 600, Easing.CubicInOut),
				compositeBox.RotateTo(180, 600, Easing.CubicInOut),
				compositeBox.FadeTo(0.4, 600, Easing.CubicInOut)
			);
			await Task.WhenAll(
				compositeBox.TranslateTo(0, 0, 600, Easing.CubicInOut),
				compositeBox.ScaleTo(1, 600, Easing.CubicInOut),
				compositeBox.RotateTo(0, 600, Easing.CubicInOut),
				compositeBox.FadeTo(1, 600, Easing.CubicInOut)
			);
			statusLabel.Text = "Composite animation complete";
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 16,
				Padding = new Thickness(24),
				Children =
				{
					statusLabel,

					SectionHeader("Basic Transforms"),
					targetBox,
					new HorizontalStackLayout
					{
						Spacing = 8,
						HorizontalOptions = LayoutOptions.Center,
						Children = { translateBtn, scaleBtn, rotateBtn, fadeBtn }
					},
					resetBtn,

					Separator(),

					SectionHeader("AnchorX / AnchorY"),
					anchorLabel,
					anchorBox,
					new HorizontalStackLayout
					{
						Spacing = 8,
						HorizontalOptions = LayoutOptions.Center,
						Children = { anchorTopLeft, anchorCenter, anchorBottomRight }
					},

					Separator(),

					SectionHeader("Composite Animation"),
					new Label { Text = "Translate + Scale + Rotate + Fade simultaneously", FontSize = 14, TextColor = Colors.Gray },
					compositeBox,
					compositeBtn,
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
