using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class LayoutsPage : ContentPage
{
	public LayoutsPage()
	{
		Title = "Layouts";

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 16,
				Padding = new Thickness(24),
				Children =
				{
					SectionHeader("VerticalStackLayout"),
					new Border
					{
						Stroke = Colors.SlateGray,
						StrokeThickness = 1,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Padding = new Thickness(12),
						Content = new VerticalStackLayout
						{
							Spacing = 6,
							Children =
							{
								ColorBlock("Item 1", Colors.CornflowerBlue),
								ColorBlock("Item 2", Colors.MediumSeaGreen),
								ColorBlock("Item 3", Colors.Coral),
							}
						}
					},

					SectionHeader("HorizontalStackLayout"),
					new Border
					{
						Stroke = Colors.SlateGray,
						StrokeThickness = 1,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Padding = new Thickness(12),
						Content = new HorizontalStackLayout
						{
							Spacing = 8,
							Children =
							{
								ColorBlock("A", Colors.DodgerBlue, 80),
								ColorBlock("B", Colors.Orange, 80),
								ColorBlock("C", Colors.MediumPurple, 80),
								ColorBlock("D", Colors.Teal, 80),
							}
						}
					},

					SectionHeader("Nested Layouts"),
					new Border
					{
						Stroke = Colors.SlateGray,
						StrokeThickness = 1,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Padding = new Thickness(12),
						Content = CreateNestedGrid(),
					},

					SectionHeader("Bordered Container"),
					new Border
					{
						Stroke = Colors.DarkOrange,
						StrokeThickness = 1,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Padding = new Thickness(16),
						Content = new VerticalStackLayout
						{
							Spacing = 4,
							Children =
							{
								new Label { Text = "Inside a Border", FontSize = 16, FontAttributes = FontAttributes.Bold },
								new Label { Text = "Borders provide a container with custom stroke and thickness.", FontSize = 13, TextColor = Colors.Gray },
							}
						}
					},

					SectionHeader("Border"),
					new Border
					{
						Stroke = Colors.MediumPurple,
						StrokeThickness = 2,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Padding = new Thickness(16),
						Content = new HorizontalStackLayout
						{
							Spacing = 12,
							Children =
							{
								new Label { Text = "🎨", FontSize = 32 },
								new VerticalStackLayout
								{
									Children =
									{
										new Label { Text = "Styled Border", FontSize = 16, FontAttributes = FontAttributes.Bold },
										new Label { Text = "Borders can have custom stroke color and thickness.", FontSize = 13, TextColor = Colors.Gray },
									}
								}
							}
						}
					},

					SectionHeader("Rounded Borders"),
					new Border
					{
						Stroke = Colors.DodgerBlue,
						StrokeThickness = 2,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
						Padding = new Thickness(16),
						Content = new Label { Text = "Uniform 12px corners", FontSize = 14 }
					},
					new Border
					{
						Stroke = Colors.MediumPurple,
						StrokeThickness = 2,
						BackgroundColor = Colors.MediumPurple.WithAlpha(0.1f),
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20, 4, 20, 4) },
						Padding = new Thickness(16),
						Content = new Label { Text = "Asymmetric corners (20/4/20/4)", FontSize = 14 }
					},
					new Border
					{
						Stroke = Colors.Transparent,
						StrokeThickness = 0,
						BackgroundColor = Colors.Teal,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(24) },
						Padding = new Thickness(20),
						Content = new Label { Text = "Pill-style rounded", FontSize = 14, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center }
					},

					SectionHeader("Deeply Nested"),
					new Border
					{
						Stroke = Colors.Red,
						StrokeThickness = 2,
						StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
						Padding = new Thickness(8),
						Content = new Border
						{
							Stroke = Colors.Orange,
							StrokeThickness = 2,
							StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
							Padding = new Thickness(8),
							Content = new Border
							{
								Stroke = Colors.Green,
								StrokeThickness = 2,
								StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
								Padding = new Thickness(8),
								Content = new Border
								{
									Stroke = Colors.Blue,
									StrokeThickness = 2,
									StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
									Padding = new Thickness(12),
									Content = new Label
									{
										Text = "4 levels deep!",
										FontSize = 14,
										HorizontalTextAlignment = TextAlignment.Center,
									}
								}
							}
						}
					},
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

	static Grid CreateNestedGrid()
	{
		var grid = new Grid
		{
			RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
			ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
			RowSpacing = 8,
			ColumnSpacing = 8,
		};
		var tl = ColorBlock("Top-Left", Colors.Salmon);
		var tr = ColorBlock("Top-Right", Colors.SkyBlue);
		var bl = ColorBlock("Bottom-Left", Colors.PaleGreen);
		var br = ColorBlock("Bottom-Right", Colors.Plum);
		Grid.SetRow(tl, 0); Grid.SetColumn(tl, 0);
		Grid.SetRow(tr, 0); Grid.SetColumn(tr, 1);
		Grid.SetRow(bl, 1); Grid.SetColumn(bl, 0);
		Grid.SetRow(br, 1); Grid.SetColumn(br, 1);
		grid.Children.Add(tl);
		grid.Children.Add(tr);
		grid.Children.Add(bl);
		grid.Children.Add(br);
		return grid;
	}

	static Border ColorBlock(string text, Color bg, int width = 0)
	{
		var border = new Border
		{
			BackgroundColor = bg,
			Padding = new Thickness(12, 8),
			Stroke = bg,
			StrokeThickness = 0,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) },
			Content = new Label
			{
				Text = text,
				TextColor = Colors.White,
				FontSize = 14,
				FontAttributes = FontAttributes.Bold,
				HorizontalTextAlignment = TextAlignment.Center,
			}
		};
		if (width > 0) border.WidthRequest = width;
		return border;
	}
}
