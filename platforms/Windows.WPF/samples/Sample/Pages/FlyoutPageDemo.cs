using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class FlyoutPageDemo : ContentPage
{
	private readonly Label _detailTitle;
	private readonly VerticalStackLayout _detailContent;

	public FlyoutPageDemo()
	{
		Title = "FlyoutPage Demo";

		_detailTitle = new Label { Text = "Welcome", FontSize = 20, FontAttributes = FontAttributes.Bold };
		_detailContent = new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label { Text = "Select an item from the sidebar to see its content here.", FontSize = 14, TextColor = Colors.Gray },
			}
		};

		var menuItems = new (string icon, string title, Color color)[]
		{
			("📥", "Inbox", Colors.DodgerBlue),
			("⭐", "Starred", Colors.Gold),
			("📤", "Sent", Colors.MediumSeaGreen),
			("📝", "Drafts", Colors.Orange),
			("🗑️", "Trash", Colors.Red),
			("📁", "Archive", Colors.SlateGray),
		};

		var menuStack = new VerticalStackLayout { Spacing = 0 };

		menuStack.Children.Add(new Label
		{
			Text = "📬 Mail",
			FontSize = 18,
			FontAttributes = FontAttributes.Bold,
			Padding = new Thickness(16, 16, 16, 8),
		});
		menuStack.Children.Add(new Border { HeightRequest = 1, BackgroundColor = Colors.Gray, Opacity = 0.3, StrokeThickness = 0 });

		foreach (var (icon, title, color) in menuItems)
		{
			var btn = new Button
			{
				Text = $"{icon}  {title}",
				BackgroundColor = Colors.Transparent,
				FontSize = 14,
				HorizontalOptions = LayoutOptions.Fill,
			};
			var capturedTitle = title;
			var capturedIcon = icon;
			var capturedColor = color;
			btn.Clicked += (s, e) => ShowDetail(capturedIcon, capturedTitle, capturedColor);
			menuStack.Children.Add(btn);
		}

		var detailPanel = new VerticalStackLayout
		{
			Spacing = 12,
			Padding = new Thickness(24),
			Children =
			{
				_detailTitle,
				_detailContent,
			}
		};

		Content = new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label
				{
					Text = "FlyoutPage Demo",
					FontSize = 24,
					FontAttributes = FontAttributes.Bold,
					Padding = new Thickness(24, 24, 24, 0),
				},
				new Label
				{
					Text = "Simulates a FlyoutPage with sidebar + detail. The real FlyoutPageHandler uses NSSplitView.",
					FontSize = 13,
					TextColor = Colors.Gray,
					Padding = new Thickness(24, 0),
				},
				new HorizontalStackLayout
				{
					Spacing = 0,
					Children =
					{
						new ScrollView
						{
							WidthRequest = 200,
							HeightRequest = 400,
							Content = menuStack,
						},
						new Border { WidthRequest = 2, BackgroundColor = Colors.Gray, Opacity = 0.3, HeightRequest = 400, StrokeThickness = 0 },
						new ScrollView
						{
							HeightRequest = 400,
							Content = detailPanel,
						},
					}
				},
			}
		};
	}

	void ShowDetail(string icon, string title, Color color)
	{
		_detailTitle.Text = $"{icon} {title}";
		_detailTitle.TextColor = color;

		_detailContent.Children.Clear();

		var messageCount = new Random().Next(3, 12);
		_detailContent.Children.Add(new Label
		{
			Text = $"{messageCount} messages",
			FontSize = 14,
			TextColor = Colors.Gray,
		});

		for (int i = 1; i <= Math.Min(messageCount, 5); i++)
		{
			var senderColor = i % 3 == 0 ? Colors.Coral : i % 2 == 0 ? Colors.CornflowerBlue : Colors.MediumSeaGreen;
			_detailContent.Children.Add(new Border
			{
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
				Stroke = Color.FromArgb("#e0e0e0"),
				StrokeThickness = 1,
				BackgroundColor = Colors.White,
				Padding = new Thickness(12, 8),
				Content = new VerticalStackLayout
				{
					Spacing = 4,
					Children =
					{
						new HorizontalStackLayout
						{
							Spacing = 8,
							Children =
							{
								new Border
								{
									StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
									BackgroundColor = senderColor,
									WidthRequest = 24,
									HeightRequest = 24,
									StrokeThickness = 0,
									Content = new Label
									{
										Text = ((char)('A' + i - 1)).ToString(),
										TextColor = Colors.White,
										FontSize = 11,
										HorizontalTextAlignment = TextAlignment.Center,
										VerticalTextAlignment = TextAlignment.Center,
									}
								},
								new Label { Text = $"Sender {i}", FontSize = 14, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center },
							}
						},
						new Label { Text = $"Message preview for {title.ToLower()} item #{i}...", FontSize = 12, TextColor = Colors.DimGray },
					}
				}
			});
		}
	}
}
