using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

class TransformsPage : ContentPage
{
	public TransformsPage()
	{
		Title = "Transforms & Effects";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 20,
				Padding = new Thickness(24),
				Children =
				{
					new Label { Text = "Transforms & Effects", FontSize = 22, FontAttributes = FontAttributes.Bold },

					// --- Rotation ---
					new Label { Text = "Rotation", FontSize = 16, FontAttributes = FontAttributes.Bold },
					new HorizontalStackLayout
					{
						Spacing = 20,
						Children =
						{
							MakeBox(Colors.CornflowerBlue, "0°", rotation: 0),
							MakeBox(Colors.Coral, "15°", rotation: 15),
							MakeBox(Colors.MediumSeaGreen, "45°", rotation: 45),
							MakeBox(Colors.Orchid, "90°", rotation: 90),
						}
					},

					// --- Scale ---
					new Label { Text = "Scale", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 30,
						Children =
						{
							MakeBox(Colors.SteelBlue, "0.5x", scale: 0.5),
							MakeBox(Colors.Tomato, "1.0x", scale: 1.0),
							MakeBox(Colors.Gold, "1.5x", scale: 1.5),
						}
					},

					// --- ScaleX / ScaleY ---
					new Label { Text = "ScaleX / ScaleY", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 30,
						Children =
						{
							MakeBox(Colors.Peru, "ScaleX=1.5", scaleX: 1.5),
							MakeBox(Colors.Teal, "ScaleY=1.5", scaleY: 1.5),
						}
					},

					// --- Translation ---
					new Label { Text = "Translation", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 20,
						HeightRequest = 80,
						Children =
						{
							MakeBox(Colors.DodgerBlue, "No shift"),
							MakeBox(Colors.OrangeRed, "X+20 Y+10", translationX: 20, translationY: 10),
							MakeBox(Colors.MediumPurple, "X-10 Y+20", translationX: -10, translationY: 20),
						}
					},

					// --- Combined ---
					new Label { Text = "Combined (Rotate + Scale + Translate)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 40,
						HeightRequest = 100,
						Children =
						{
							MakeBox(Colors.DeepPink, "All", rotation: 30, scale: 1.3, translationX: 10, translationY: 5),
						}
					},

					// --- Shadow ---
					new Label { Text = "Shadow (box-shadow)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 30,
						Children =
						{
							new BoxView
							{
								Color = Colors.White,
								WidthRequest = 80, HeightRequest = 60,
								Shadow = new Shadow
								{
									Brush = new SolidColorBrush(Colors.Black),
									Offset = new Point(4, 4),
									Radius = 8,
									Opacity = 0.4f,
								}
							},
							new Button
							{
								Text = "Shadow Btn",
								BackgroundColor = Colors.CornflowerBlue,
								TextColor = Colors.White,
								Shadow = new Shadow
								{
									Brush = new SolidColorBrush(Colors.DarkBlue),
									Offset = new Point(3, 3),
									Radius = 6,
									Opacity = 0.5f,
								}
							},
							new Label
							{
								Text = "Shadow Label",
								FontSize = 18,
								FontAttributes = FontAttributes.Bold,
								Padding = new Thickness(12, 8),
								BackgroundColor = Colors.LightYellow,
								Shadow = new Shadow
								{
									Brush = new SolidColorBrush(Colors.Orange),
									Offset = new Point(2, 2),
									Radius = 4,
									Opacity = 0.6f,
								}
							},
						}
					},

					// --- InputTransparent ---
					new Label { Text = "InputTransparent", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					BuildInputTransparentDemo(),

					// --- AnchorX / AnchorY ---
					new Label { Text = "AnchorX/Y (transform origin)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 30,
						Children =
						{
							MakeBox(Colors.Crimson, "TopLeft", rotation: 30, anchorX: 0, anchorY: 0),
							MakeBox(Colors.ForestGreen, "Center", rotation: 30, anchorX: 0.5, anchorY: 0.5),
							MakeBox(Colors.RoyalBlue, "BotRight", rotation: 30, anchorX: 1, anchorY: 1),
						}
					},

					// --- Clip ---
					new Label { Text = "Clip (Geometry Clipping)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 20,
						Children =
						{
							new BoxView
							{
								Color = Colors.CornflowerBlue,
								WidthRequest = 80, HeightRequest = 80,
								Clip = new Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry
								{
									CornerRadius = new CornerRadius(16),
									Rect = new Rect(0, 0, 80, 80),
								},
							},
							new BoxView
							{
								Color = Colors.Coral,
								WidthRequest = 80, HeightRequest = 80,
								Clip = new Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry
								{
									CornerRadius = new CornerRadius(40),
									Rect = new Rect(0, 0, 80, 80),
								},
							},
							new BoxView
							{
								Color = Colors.MediumSeaGreen,
								WidthRequest = 80, HeightRequest = 80,
								Clip = new Microsoft.Maui.Controls.Shapes.EllipseGeometry
								{
									Center = new Point(40, 40),
									RadiusX = 40,
									RadiusY = 40,
								},
							},
							new BoxView
							{
								Color = Colors.Orchid,
								WidthRequest = 80, HeightRequest = 80,
								Clip = new Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry
								{
									CornerRadius = new CornerRadius(20, 0, 20, 0),
									Rect = new Rect(0, 0, 80, 80),
								},
							},
						}
					},
					new Label { Text = "Round 16px / Circle / Ellipse / Diagonal corners", FontSize = 11, TextColor = Colors.Gray },

					// --- Gradient Brushes ---
					new Label { Text = "Gradient Brushes", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					new HorizontalStackLayout
					{
						Spacing = 16,
						Children =
						{
							new Frame
							{
								WidthRequest = 120, HeightRequest = 80, Padding = 0,
								CornerRadius = 8, BorderColor = Colors.Transparent, HasShadow = false,
								Background = new LinearGradientBrush
								{
									StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
									GradientStops = { new GradientStop(Colors.DodgerBlue, 0), new GradientStop(Colors.MediumPurple, 1) },
								},
								Content = new Label { Text = "Linear ↘", TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center },
							},
							new Frame
							{
								WidthRequest = 120, HeightRequest = 80, Padding = 0,
								CornerRadius = 8, BorderColor = Colors.Transparent, HasShadow = false,
								Background = new LinearGradientBrush
								{
									StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
									GradientStops = { new GradientStop(Colors.OrangeRed, 0), new GradientStop(Colors.Gold, 0.5f), new GradientStop(Colors.LimeGreen, 1) },
								},
								Content = new Label { Text = "3-Stop →", TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center },
							},
							new Frame
							{
								WidthRequest = 120, HeightRequest = 80, Padding = 0,
								CornerRadius = 8, BorderColor = Colors.Transparent, HasShadow = false,
								Background = new RadialGradientBrush
								{
									Center = new Point(0.5, 0.5), Radius = 0.6,
									GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.Coral, 0.6f), new GradientStop(Colors.DarkRed, 1) },
								},
								Content = new Label { Text = "Radial", TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center },
							},
							new Frame
							{
								WidthRequest = 120, HeightRequest = 80, Padding = 0,
								CornerRadius = 40, BorderColor = Colors.Transparent, HasShadow = false,
								Background = new LinearGradientBrush
								{
									StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
									GradientStops = { new GradientStop(Colors.DeepSkyBlue, 0), new GradientStop(Colors.DeepPink, 1) },
								},
								Content = new Label { Text = "Rounded", TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center },
							},
						}
					},
					new Label { Text = "Linear diagonal / 3-stop horizontal / Radial / Rounded pill", FontSize = 11, TextColor = Colors.Gray },

					// --- ZIndex ---
					new Label { Text = "ZIndex (draw order)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) },
					BuildZIndexDemo(),
					new Label { Text = "Blue (Z=1) → Coral (Z=2) → Green (Z=3, on top)", FontSize = 11, TextColor = Colors.Gray },

					// --- Animations ---
					new Label { Text = "Animations (PlatformTicker)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 0) },
					BuildAnimationsDemo(),

					// --- ToolTip ---
					new Label { Text = "ToolTip", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 0) },
					BuildToolTipDemo(),

					// --- ContextFlyout (right-click menu) ---
					new Label { Text = "ContextFlyout (right-click)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 0) },
					BuildContextFlyoutDemo(),

					// --- FontImageSource (font icons) ---
					new Label { Text = "FontImageSource (Font Icons)", FontSize = 16, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 0) },
					BuildFontImageSourceDemo(),
				}
			}
		};
	}

	static BoxView MakeBox(Color color, string tooltip,
		double rotation = 0, double scale = 1, double scaleX = 1, double scaleY = 1,
		double translationX = 0, double translationY = 0,
		double anchorX = 0.5, double anchorY = 0.5)
	{
		return new BoxView
		{
			Color = color,
			WidthRequest = 60,
			HeightRequest = 60,
			Rotation = rotation,
			Scale = scale,
			ScaleX = scaleX,
			ScaleY = scaleY,
			TranslationX = translationX,
			TranslationY = translationY,
			AnchorX = anchorX,
			AnchorY = anchorY,
		};
	}

	static View BuildInputTransparentDemo()
	{
		var resultLabel = new Label { Text = "Click the buttons below:", FontSize = 14 };

		var normalBtn = new Button
		{
			Text = "Normal (clickable)",
			BackgroundColor = Colors.MediumSeaGreen,
			TextColor = Colors.White,
		};
		normalBtn.Clicked += (s, e) => resultLabel.Text = "✅ Normal button clicked!";

		var transparentBtn = new Button
		{
			Text = "InputTransparent",
			BackgroundColor = Colors.LightGray,
			TextColor = Colors.Gray,
			InputTransparent = true,
		};
		transparentBtn.Clicked += (s, e) => resultLabel.Text = "❌ This should NOT fire!";

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				resultLabel,
				new HorizontalStackLayout
				{
					Spacing = 12,
					Children = { normalBtn, transparentBtn }
				},
			}
		};
	}

	static View BuildZIndexDemo()
	{
		var layout = new AbsoluteLayout { HeightRequest = 110 };

		var b1 = new BoxView { Color = Colors.CornflowerBlue, WidthRequest = 80, HeightRequest = 80, ZIndex = 1 };
		var b2 = new BoxView { Color = Colors.Coral, WidthRequest = 80, HeightRequest = 80, ZIndex = 2 };
		var b3 = new BoxView { Color = Colors.MediumSeaGreen, WidthRequest = 80, HeightRequest = 80, ZIndex = 3 };

		layout.Children.Add(b1);
		AbsoluteLayout.SetLayoutBounds(b1, new Rect(0, 0, 80, 80));
		layout.Children.Add(b2);
		AbsoluteLayout.SetLayoutBounds(b2, new Rect(30, 10, 80, 80));
		layout.Children.Add(b3);
		AbsoluteLayout.SetLayoutBounds(b3, new Rect(60, 20, 80, 80));

		return layout;
	}

	static View BuildAnimationsDemo()
	{
		var animBox = new BoxView
		{
			Color = Colors.DodgerBlue,
			WidthRequest = 80,
			HeightRequest = 80,
			CornerRadius = 8,
		};

		var statusLabel = new Label { Text = "Tap a button to animate", FontSize = 12, TextColor = Colors.Gray };

		var translateBtn = new Button { Text = "TranslateTo", BackgroundColor = Colors.CornflowerBlue, TextColor = Colors.White };
		translateBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "TranslateTo(100, 0) ...";
			await animBox.TranslateTo(100, 0, 500, Easing.CubicInOut);
			await animBox.TranslateTo(0, 0, 500, Easing.CubicInOut);
			statusLabel.Text = "TranslateTo done ✅";
		};

		var fadeBtn = new Button { Text = "FadeTo", BackgroundColor = Colors.Coral, TextColor = Colors.White };
		fadeBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "FadeTo(0.2) ...";
			await animBox.FadeTo(0.2, 500);
			await animBox.FadeTo(1.0, 500);
			statusLabel.Text = "FadeTo done ✅";
		};

		var scaleBtn = new Button { Text = "ScaleTo", BackgroundColor = Colors.MediumSeaGreen, TextColor = Colors.White };
		scaleBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "ScaleTo(1.5) ...";
			await animBox.ScaleTo(1.5, 400, Easing.SpringOut);
			await animBox.ScaleTo(1.0, 400, Easing.SpringIn);
			statusLabel.Text = "ScaleTo done ✅";
		};

		var rotateBtn = new Button { Text = "RotateTo", BackgroundColor = Colors.Orchid, TextColor = Colors.White };
		rotateBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "RotateTo(360) ...";
			animBox.Rotation = 0;
			await animBox.RotateTo(360, 800, Easing.CubicInOut);
			animBox.Rotation = 0;
			statusLabel.Text = "RotateTo done ✅";
		};

		var allBtn = new Button { Text = "All Combined", BackgroundColor = Colors.DeepPink, TextColor = Colors.White };
		allBtn.Clicked += async (s, e) =>
		{
			statusLabel.Text = "All animations ...";
			await Task.WhenAll(
				animBox.TranslateTo(60, -20, 600, Easing.CubicInOut),
				animBox.ScaleTo(1.3, 600, Easing.CubicInOut),
				animBox.RotateTo(180, 600, Easing.CubicInOut),
				animBox.FadeTo(0.5, 600)
			);
			await Task.WhenAll(
				animBox.TranslateTo(0, 0, 600, Easing.CubicInOut),
				animBox.ScaleTo(1.0, 600, Easing.CubicInOut),
				animBox.RotateTo(360, 600, Easing.CubicInOut),
				animBox.FadeTo(1.0, 600)
			);
			animBox.Rotation = 0;
			statusLabel.Text = "All animations done ✅";
		};

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				animBox,
				new HorizontalStackLayout
				{
					Spacing = 8,
					Children = { translateBtn, fadeBtn, scaleBtn, rotateBtn, allBtn }
				},
				statusLabel,
			}
		};
	}

	static View BuildToolTipDemo()
	{
		var btn1 = new Button { Text = "Hover me!", BackgroundColor = Colors.SteelBlue, TextColor = Colors.White };
		ToolTipProperties.SetText(btn1, "This is a ToolTip on a Button");

		var label1 = new Label { Text = "Label with tooltip", FontSize = 14, Padding = new Thickness(8, 4), BackgroundColor = Colors.LightGoldenrodYellow };
		ToolTipProperties.SetText(label1, "ToolTip on a Label — hover to see");

		var entry1 = new Entry { Placeholder = "Entry with tooltip" };
		ToolTipProperties.SetText(entry1, "Enter some text here");

		return new HorizontalStackLayout { Spacing = 12, Children = { btn1, label1, entry1 } };
	}

	static View BuildContextFlyoutDemo()
	{
		var resultLabel = new Label { Text = "Right-click the box below:", FontSize = 12, TextColor = Colors.Gray };

		var box = new BoxView
		{
			Color = Colors.SlateBlue,
			WidthRequest = 150,
			HeightRequest = 80,
			CornerRadius = 8,
		};

		var flyout = new MenuFlyout
		{
			new MenuFlyoutItem { Text = "Cut" },
			new MenuFlyoutItem { Text = "Copy" },
			new MenuFlyoutItem { Text = "Paste" },
			new MenuFlyoutSeparator(),
			new MenuFlyoutItem { Text = "Delete" },
		};

		foreach (var item in flyout.OfType<MenuFlyoutItem>())
		{
			var captured = item;
			item.Clicked += (s, e) => resultLabel.Text = $"Selected: {captured.Text} ✅";
		}

		FlyoutBase.SetContextFlyout(box, flyout);

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children = { resultLabel, box, new Label { Text = "Right-click the purple box for a context menu", FontSize = 11, TextColor = Colors.Gray } }
		};
	}

	static View BuildFontImageSourceDemo()
	{
		// Unicode symbols that work with any font
		return new HorizontalStackLayout
		{
			Spacing = 16,
			Children =
			{
				new Image
				{
					Source = new FontImageSource { Glyph = "★", Color = Colors.Gold, Size = 32 },
					WidthRequest = 40, HeightRequest = 40,
				},
				new Image
				{
					Source = new FontImageSource { Glyph = "♥", Color = Colors.Red, Size = 32 },
					WidthRequest = 40, HeightRequest = 40,
				},
				new Image
				{
					Source = new FontImageSource { Glyph = "⚡", Color = Colors.Orange, Size = 32 },
					WidthRequest = 40, HeightRequest = 40,
				},
				new Image
				{
					Source = new FontImageSource { Glyph = "✓", Color = Colors.Green, Size = 32 },
					WidthRequest = 40, HeightRequest = 40,
				},
				new Button
				{
					Text = "Font Icon Button",
					ImageSource = new FontImageSource { Glyph = "⚙", Color = Colors.DodgerBlue, Size = 20 },
					BackgroundColor = Colors.White,
					TextColor = Colors.DodgerBlue,
				},
				new Label { Text = "← Unicode glyphs rendered as images via Cairo+Pango", FontSize = 11, TextColor = Colors.Gray, VerticalTextAlignment = TextAlignment.Center },
			}
		};
	}
}
