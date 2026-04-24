using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	// Plain View — no Component<TState> so button handlers never trigger
	// a re-render that would dispose in-flight animations.
	public class TransformsPage : View
	{
		Border targetBox;
		Border anchorBox;
		Border compositeBox;
		double anchorRotation = 0;

		[Body]
		View body()
		{
			// Explicitly set initial values for all animatable properties so
			// the animation system has a concrete start value to lerp from.
			targetBox = Border(new Spacer())
				.Background(Colors.DodgerBlue)
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(width: 100, height: 100)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.TranslationX(0).TranslationY(0)
				.Rotation(0)
				.Scale(1)
				.Opacity(1);

			var translateBtn = Button("TranslateTo (100, 0)", () =>
			{
				targetBox?.TranslateTo(100, 0, 0.5, Easing.CubicInOut);
			});

			var scaleBtn = Button("ScaleTo 1.5x", () =>
			{
				targetBox?.ScaleTo(scale: 1.5, duration: 0.5, easing: Easing.CubicInOut);
			});

			var rotateBtn = Button("RotateTo 90", () =>
			{
				targetBox?.RotateTo(90, 0.5, Easing.CubicInOut);
			});

			var fadeBtn = Button("FadeTo 0.3", () =>
			{
				targetBox?.FadeTo(0.3, 0.5, Easing.CubicInOut);
			});

			var resetBtn = Button("Reset All", () =>
			{
				targetBox?.TranslateTo(0, 0, 0.3);
				targetBox?.ScaleTo(scale: 1, duration: 0.3);
				targetBox?.RotateTo(0, 0.3);
				targetBox?.FadeTo(1, 0.3);
			}).Background(Colors.Crimson).Color(Colors.White);

			anchorBox = Border(new Spacer())
				.Background(Colors.MediumPurple)
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(width: 80, height: 80)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.Rotation(0);

			var anchorTopLeftBtn = Button("Anchor (0, 0)", () =>
			{
				anchorRotation += 360;
				anchorBox?.AnchorX(0);
				anchorBox?.AnchorY(0);
				anchorBox?.RotateTo(anchorRotation, 0.8);
			});

			var anchorCenterBtn = Button("Anchor (0.5, 0.5)", () =>
			{
				anchorRotation += 360;
				anchorBox?.AnchorX(0.5);
				anchorBox?.AnchorY(0.5);
				anchorBox?.RotateTo(anchorRotation, 0.8);
			});

			var anchorBottomRightBtn = Button("Anchor (1, 1)", () =>
			{
				anchorRotation += 360;
				anchorBox?.AnchorX(1);
				anchorBox?.AnchorY(1);
				anchorBox?.RotateTo(anchorRotation, 0.8);
			});

			compositeBox = Border(new Spacer())
				.Background(Colors.Coral)
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(width: 80, height: 80)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.TranslationX(0)
				.Rotation(0)
				.ScaleX(1).ScaleY(1)
				.Opacity(1);

			var compositeBtn = Button("Run Composite Animation", () =>
			{
				compositeBox?.Animate(v =>
				{
					v.TranslationX(80);
					v.ScaleX(1.5);
					v.ScaleY(1.5);
					v.Rotation(180);
					v.Opacity(0.4);
				}, duration: 0.6, autoReverses: true);
			});

			return GalleryPageHelpers.Scaffold("Transforms",
				GalleryPageHelpers.BodyText("Use the buttons below to animate the box")
					.Color(Colors.Grey)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
				GalleryPageHelpers.Section("Basic Transforms",
					targetBox,
					GalleryPageHelpers.ButtonRow(8,
						translateBtn,
						scaleBtn,
						rotateBtn,
						fadeBtn
					),
					resetBtn
				),
				GalleryPageHelpers.Section("AnchorX / AnchorY",
					GalleryPageHelpers.BodyText("Tap an anchor button to set the rotation pivot point"),
					anchorBox,
					GalleryPageHelpers.ButtonRow(8,
						anchorTopLeftBtn,
						anchorCenterBtn,
						anchorBottomRightBtn
					)
				),
				GalleryPageHelpers.Section("Composite Animation",
					GalleryPageHelpers.BodyText("Translate + Scale + Rotate + Fade simultaneously")
						.Color(Colors.Grey),
					compositeBox,
					compositeBtn
				)
			);
		}
	}
}
