using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

/// <summary>
/// Demonstrates RefreshView (pull-to-refresh / refresh button),
/// SwipeView (swipe-to-reveal actions), and gesture recognizers.
/// </summary>
public class RefreshSwipePage : ContentPage
{
	readonly VerticalStackLayout _itemsStack;
	readonly Label _statusLabel;
	int _refreshCount;
	bool _isRefreshing;

	public RefreshSwipePage()
	{
		Title = "Refresh & Swipe";

		_statusLabel = new Label
		{
			Text = "Tap the refresh button to reload items",
			HorizontalOptions = LayoutOptions.Center,
			TextColor = Colors.DimGray,
			FontSize = 12,
		};

		_itemsStack = new VerticalStackLayout { Spacing = 1 };
		LoadItems();

		RefreshView refreshView = null!;
		refreshView = new RefreshView
		{
			Content = new ScrollView
			{
				Content = _itemsStack,
				HeightRequest = 200,
			},
			RefreshColor = Colors.DodgerBlue,
			HeightRequest = 240,
			Command = new Command(async () =>
			{
				if (_isRefreshing) return;
				_isRefreshing = true;
				try
				{
					await Task.Delay(1200);
					_refreshCount++;
					LoadItems();
					_statusLabel.Text = $"Refreshed {_refreshCount} time(s) ✅";
				}
				finally
				{
					_isRefreshing = false;
					refreshView.IsRefreshing = false;
				}
			}),
		};

		// SwipeView demo
		var swipeStatusLabel = new Label
		{
			Text = "← Swipe the card left or right →",
			TextColor = Colors.Gray,
			FontSize = 12,
			HorizontalTextAlignment = TextAlignment.Center,
		};

		var swipeDemo = new SwipeView
		{
			HeightRequest = 80,
			LeftItems = new SwipeItems
			{
				new SwipeItem
				{
					Text = "Archive",
					BackgroundColor = Colors.MediumSeaGreen,
					Command = new Command(() => swipeStatusLabel.Text = "✅ Archived!"),
				},
			},
			RightItems = new SwipeItems
			{
				new SwipeItem
				{
					Text = "Delete",
					BackgroundColor = Colors.Crimson,
					Command = new Command(() => swipeStatusLabel.Text = "🗑️ Deleted!"),
				},
				new SwipeItem
				{
					Text = "Flag",
					BackgroundColor = Colors.Orange,
					Command = new Command(() => swipeStatusLabel.Text = "🚩 Flagged!"),
				},
			},
			Content = new Border
			{
				Stroke = Colors.DodgerBlue,
				StrokeThickness = 1,
				Padding = new Thickness(16, 12),
				Content = new VerticalStackLayout
				{
					Children =
					{
						new Label { Text = "Swipeable Card", FontAttributes = FontAttributes.Bold, FontSize = 15 },
						new Label { Text = "Drag left for Delete/Flag, right for Archive", TextColor = Colors.Gray, FontSize = 12 },
					}
				}
			},
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(16),
				Spacing = 16,
				Children =
				{
					new Label { Text = "RefreshView", FontSize = 20, FontAttributes = FontAttributes.Bold },
					_statusLabel,
					refreshView,

					new BoxView { HeightRequest = 1, Color = Colors.LightGray },

					new Label { Text = "SwipeView", FontSize = 20, FontAttributes = FontAttributes.Bold },
					swipeDemo,
					swipeStatusLabel,

					new BoxView { HeightRequest = 1, Color = Colors.LightGray },

					BuildPointerGestureDemo(),

					new BoxView { HeightRequest = 1, Color = Colors.LightGray },

					BuildSwipeGestureDemo(),

					new BoxView { HeightRequest = 1, Color = Colors.LightGray },

					BuildPinchGestureDemo(),
				}
			}
		};
	}

	void LoadItems()
	{
		var rng = new Random();
		for (int i = 0; i < 10; i++)
		{
			var text = $"  Item {i + 1}  —  value: {rng.Next(100, 999)}";
			if (i < _itemsStack.Children.Count && _itemsStack.Children[i] is Label existing)
			{
				existing.Text = text;
			}
			else
			{
				_itemsStack.Children.Add(new Label
				{
					Text = text,
					Padding = new Thickness(8, 6),
				});
			}
		}
	}

	static View BuildPointerGestureDemo()
	{
		var stateLabel = new Label { Text = "Outside", FontSize = 14, FontAttributes = FontAttributes.Bold };

		var trackingBox = new BoxView
		{
			Color = Colors.CornflowerBlue,
			WidthRequest = 200,
			HeightRequest = 80,
			HorizontalOptions = LayoutOptions.Start,
		};

		var pointerGesture = new PointerGestureRecognizer();
		pointerGesture.PointerEnteredCommand = new Command(() =>
		{
			stateLabel.Text = "🟢 Inside";
			stateLabel.TextColor = Colors.Green;
			trackingBox.Color = Colors.MediumSeaGreen;
		});
		pointerGesture.PointerExitedCommand = new Command(() =>
		{
			stateLabel.Text = "🔴 Outside";
			stateLabel.TextColor = Colors.Red;
			trackingBox.Color = Colors.CornflowerBlue;
		});
		pointerGesture.PointerPressedCommand = new Command(() =>
		{
			stateLabel.Text = "⬇️ Pressed";
			trackingBox.Color = Colors.DarkSlateBlue;
		});
		pointerGesture.PointerReleasedCommand = new Command(() =>
		{
			stateLabel.Text = "⬆️ Released";
			trackingBox.Color = Colors.MediumSeaGreen;
		});
		trackingBox.GestureRecognizers.Add(pointerGesture);

		return new VerticalStackLayout
		{
			Spacing = 6,
			Children =
			{
				new Label { Text = "Pointer Gesture", FontSize = 20, FontAttributes = FontAttributes.Bold },
				new Label { Text = "Hover, click, and release on the box below", FontSize = 12, TextColor = Colors.Gray },
				stateLabel,
				trackingBox,
			}
		};
	}

	static View BuildSwipeGestureDemo()
	{
		var resultLabel = new Label { Text = "No swipe detected", FontSize = 14 };

		var swipeBox = new BoxView
		{
			Color = Colors.Coral,
			WidthRequest = 200,
			HeightRequest = 80,
			HorizontalOptions = LayoutOptions.Start,
		};

		foreach (var dir in new[] { SwipeDirection.Left, SwipeDirection.Right, SwipeDirection.Up, SwipeDirection.Down })
		{
			var swipe = new SwipeGestureRecognizer { Direction = dir };
			swipe.Swiped += (s, e) =>
			{
				string arrow = e.Direction switch
				{
					SwipeDirection.Left => "⬅️",
					SwipeDirection.Right => "➡️",
					SwipeDirection.Up => "⬆️",
					SwipeDirection.Down => "⬇️",
					_ => "?",
				};
				resultLabel.Text = $"{arrow} Swiped {e.Direction}!";
				resultLabel.TextColor = Colors.DodgerBlue;
			};
			swipeBox.GestureRecognizers.Add(swipe);
		}

		return new VerticalStackLayout
		{
			Spacing = 6,
			Children =
			{
				new Label { Text = "Swipe Gesture", FontSize = 20, FontAttributes = FontAttributes.Bold },
				new Label { Text = "Drag quickly on the box below", FontSize = 12, TextColor = Colors.Gray },
				resultLabel,
				swipeBox,
			}
		};
	}

	static View BuildPinchGestureDemo()
	{
		var scaleLabel = new Label { Text = "Scale: 1.00x", FontSize = 14 };

		var target = new BoxView
		{
			Color = Colors.MediumSlateBlue,
			WidthRequest = 100,
			HeightRequest = 100,
			HorizontalOptions = LayoutOptions.Start,
		};

		var pinch = new PinchGestureRecognizer();
		pinch.PinchUpdated += (s, e) =>
		{
			switch (e.Status)
			{
				case GestureStatus.Running:
					target.Scale = Math.Clamp(e.Scale, 0.3, 3.0);
					scaleLabel.Text = $"Scale: {target.Scale:F2}x";
					break;
				case GestureStatus.Completed:
					scaleLabel.Text = $"Done at {target.Scale:F2}x";
					break;
				case GestureStatus.Canceled:
					target.Scale = 1.0;
					scaleLabel.Text = "Scale: 1.00x";
					break;
			}
		};
		target.GestureRecognizers.Add(pinch);

		return new VerticalStackLayout
		{
			Spacing = 6,
			Children =
			{
				new Label { Text = "Pinch Gesture", FontSize = 20, FontAttributes = FontAttributes.Bold },
				new Label { Text = "Use Ctrl+scroll to zoom the box", FontSize = 12, TextColor = Colors.Gray },
				scaleLabel,
				target,
			}
		};
	}
}
