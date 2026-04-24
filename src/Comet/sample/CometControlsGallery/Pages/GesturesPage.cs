using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class GesturesPage : View
	{
		// Tap: discrete gesture — State<T> with Text binding is fine
		readonly Reactive<int> tapCount = 0;
		View tapBox;

		// Pan: continuous gesture — plain fields + direct view updates avoid rebuild mid-drag
		double panTotalX, panTotalY;
		View panBox;

		// Swipe: discrete gesture — State<T> with Text binding is fine
		readonly Reactive<string> swipeResult = "Swipe result: (none)";

		// Pinch: continuous gesture — plain field for scale, direct view update
		double currentPinchScale = 1;
		readonly Reactive<string> pinchLabel = "Scale: 1.00";
		View pinchBox;

		// Pointer: continuous gesture — direct view updates avoid rebuild mid-hover
		readonly Reactive<string> pointerLabel = "Hover over the box";
		View pointerBox;

		[Body]
		View body()
		{
			tapBox = Border(new Spacer())
				.Background(Colors.DodgerBlue)
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(width: 120, height: 80)
				.OnTap(_ =>
				{
					tapCount.Value++;
					if (tapBox != null)
						tapBox.Background(tapCount.Value % 2 == 0 ? Colors.DodgerBlue : Colors.Coral);
				});

			panBox = Border(new Spacer())
				.Background(Colors.MediumSeaGreen)
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(width: 80, height: 80);

			var panContainer = Border(panBox)
				.Background(Color.FromArgb("#F0F0F0"))
				.StrokeThickness(0)
				.Frame(height: 120)
				.OnPan(gesture =>
				{
					if (gesture.Status == Comet.GestureStatus.Running)
					{
						if (panBox != null)
						{
							panBox.TranslationX(panTotalX + gesture.TotalX);
							panBox.TranslationY(panTotalY + gesture.TotalY);
						}
					}
					else if (gesture.Status == Comet.GestureStatus.Completed)
					{
						panTotalX += gesture.TotalX;
						panTotalY += gesture.TotalY;
					}
				});

			var swipeBox = Border(new Spacer())
				.Background(Color.FromArgb("#E8F0FE"))
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(height: 80)
				.AddGesture(new SwipeGesture(_ => swipeResult.Value = "Swiped: Left") { Direction = Comet.SwipeDirection.Left })
				.AddGesture(new SwipeGesture(_ => swipeResult.Value = "Swiped: Right") { Direction = Comet.SwipeDirection.Right })
				.AddGesture(new SwipeGesture(_ => swipeResult.Value = "Swiped: Up") { Direction = Comet.SwipeDirection.Up })
				.AddGesture(new SwipeGesture(_ => swipeResult.Value = "Swiped: Down") { Direction = Comet.SwipeDirection.Down });

			pinchBox = Border(new Spacer())
				.Background(Colors.MediumPurple)
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(width: 100, height: 100)
				.OnPinch(gesture =>
				{
					if (gesture.Status == Comet.GestureStatus.Running)
					{
						var newScale = currentPinchScale * gesture.Scale;
						if (pinchBox != null)
							pinchBox.Scale(newScale);
						pinchLabel.Value = $"Scale: {newScale:F2}";
					}
					else if (gesture.Status == Comet.GestureStatus.Completed)
					{
						currentPinchScale *= gesture.Scale;
					}
				});

			pointerBox = Border(new Spacer())
				.Background(Colors.SteelBlue)
				.CornerRadius(8)
				.StrokeThickness(0)
				.Frame(width: 150, height: 80)
				.AddGesture(new PointerGesture
				{
					PointerEntered = (_, _) =>
					{
						pointerLabel.Value = "Pointer: Entered";
						if (pointerBox != null)
							pointerBox.Background(Colors.Orange);
					},
					PointerExited = (_, _) =>
					{
						pointerLabel.Value = "Pointer: Exited";
						if (pointerBox != null)
							pointerBox.Background(Colors.SteelBlue);
					},
					PointerMoved = (_, point) =>
					{
						pointerLabel.Value = $"Pointer: Moved ({point.X:F0}, {point.Y:F0})";
					}
				});

			return GalleryPageHelpers.Scaffold("Gestures",
				GalleryPageHelpers.Section("TapGestureRecognizer",
					Text(() => $"Tap the box! Taps: {tapCount.Value}").FontSize(14),
					tapBox
				),
				GalleryPageHelpers.Section("PanGestureRecognizer",
					GalleryPageHelpers.BodyText("Click and drag in the gray area:"),
					panContainer
				),
				GalleryPageHelpers.Section("SwipeGestureRecognizer",
					GalleryPageHelpers.BodyText("Click and drag quickly, then release"),
					swipeBox,
					Text(() => swipeResult.Value)
						.FontSize(16)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.DodgerBlue)
				),
				GalleryPageHelpers.Section("PinchGestureRecognizer",
					Text(() => pinchLabel.Value).FontSize(14),
					pinchBox
				),
				GalleryPageHelpers.Section("PointerGestureRecognizer",
					Text(() => pointerLabel.Value).FontSize(14),
					pointerBox
				)
			);
		}
	}
}
