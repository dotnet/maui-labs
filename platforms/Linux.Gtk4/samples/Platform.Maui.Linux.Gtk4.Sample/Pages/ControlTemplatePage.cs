using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Platform.Maui.Linux.Gtk4.Sample.Pages;

class ControlTemplatePage : ContentPage
{
	public ControlTemplatePage()
	{
		Title = "ControlTemplate & ContentPresenter";
		var stack = new VerticalStackLayout { Spacing = 16, Padding = 20 };

		stack.Add(new Label
		{
			Text = "🧩 ControlTemplate & ContentPresenter",
			FontSize = 22,
			FontAttributes = FontAttributes.Bold,
		});

		// Section 1: Basic ControlTemplate with ContentPresenter
		stack.Add(CreateSection("1. Basic ControlTemplate",
			"A ContentView with a ControlTemplate that wraps content in a colored border.",
			CreateBasicTemplateDemo()));

		// Section 2: TemplatedView (CardView)
		stack.Add(CreateSection("2. Custom TemplatedView (CardView)",
			"A custom CardView control using ControlTemplate with header, content, and footer.",
			CreateCardViewDemo()));

		// Section 3: Multiple ContentPresenters
		stack.Add(CreateSection("3. ContentPresenter in Different Templates",
			"Same content rendered with different visual templates.",
			CreateMultiTemplateDemo()));

		// Section 4: RadioButton with ControlTemplate
		stack.Add(CreateSection("4. RadioButton with Custom Template",
			"RadioButtons using a ControlTemplate for visual appearance.",
			CreateRadioButtonTemplateDemo()));

		Content = new ScrollView { Content = stack };
	}

	static View CreateSection(string title, string description, View content)
	{
		return new VerticalStackLayout
		{
			Spacing = 8,
			Children =
			{
				new Label { Text = title, FontSize = 16, FontAttributes = FontAttributes.Bold },
				new Label { Text = description, FontSize = 12, TextColor = Colors.Gray },
				content,
			}
		};
	}

	static View CreateBasicTemplateDemo()
	{
		var template = new ControlTemplate(() =>
		{
			var border = new Border
			{
				Stroke = Colors.DodgerBlue,
				StrokeThickness = 2,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
				Padding = 16,
				BackgroundColor = Color.FromArgb("#1A3498DB"),
				Content = new VerticalStackLayout
				{
					Spacing = 8,
					Children =
					{
						new Label
						{
							Text = "📦 Templated Container",
							FontSize = 14,
							FontAttributes = FontAttributes.Bold,
							TextColor = Colors.DodgerBlue,
						},
						new ContentPresenter(),
					}
				}
			};
			return border;
		});

		var contentView = new ContentView
		{
			ControlTemplate = template,
			Content = new VerticalStackLayout
			{
				Spacing = 4,
				Children =
				{
					new Label { Text = "This content is provided by the ContentView." },
					new Label { Text = "The blue border + header comes from the ControlTemplate." },
					new Button { Text = "Templated Button", BackgroundColor = Colors.DodgerBlue, TextColor = Colors.White },
				}
			}
		};

		return contentView;
	}

	static View CreateCardViewDemo()
	{
		var card = new CardView
		{
			CardTitle = "Weather Report",
			CardDescription = "Today's forecast: Sunny with clouds, high of 24°C. Light winds from the SW at 12 km/h.",
			BorderColor = Colors.Orange,
		};

		return card;
	}

	static View CreateMultiTemplateDemo()
	{
		var simpleTemplate = new ControlTemplate(() =>
		{
			return new VerticalStackLayout
			{
				Spacing = 4,
				Children =
				{
					new Label { Text = "Simple Template:", FontAttributes = FontAttributes.Bold },
					new ContentPresenter(),
				}
			};
		});

		var borderedTemplate = new ControlTemplate(() =>
		{
			return new Border
			{
				Stroke = Colors.Green,
				StrokeThickness = 2,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
				Padding = 12,
				Content = new VerticalStackLayout
				{
					Spacing = 4,
					Children =
					{
						new Label { Text = "🟢 Bordered Template:", FontAttributes = FontAttributes.Bold, TextColor = Colors.Green },
						new ContentPresenter(),
					}
				}
			};
		});

		var stack = new VerticalStackLayout { Spacing = 12 };

		var view1 = new ContentView
		{
			ControlTemplate = simpleTemplate,
			Content = new Label { Text = "Hello from template A!" },
		};

		var view2 = new ContentView
		{
			ControlTemplate = borderedTemplate,
			Content = new Label { Text = "Hello from template B!" },
		};

		stack.Add(view1);
		stack.Add(view2);
		return stack;
	}

	static View CreateRadioButtonTemplateDemo()
	{
		var radioTemplate = new ControlTemplate(() =>
		{
			var grid = new Grid
			{
				ColumnDefinitions =
				{
					new ColumnDefinition(GridLength.Auto),
					new ColumnDefinition(GridLength.Star),
				},
				ColumnSpacing = 8,
			};

			var indicator = new Border
			{
				WidthRequest = 20,
				HeightRequest = 20,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
				Stroke = Colors.DodgerBlue,
				StrokeThickness = 2,
				BackgroundColor = Colors.Transparent,
			};
			grid.Add(indicator, 0, 0);

			var presenter = new ContentPresenter();
			grid.Add(presenter, 1, 0);

			return grid;
		});

		var group = new VerticalStackLayout { Spacing = 8 };
		foreach (var option in new[] { "Option A", "Option B", "Option C" })
		{
			var rb = new RadioButton
			{
				Content = option,
				GroupName = "TemplatedGroup",
			};
			group.Add(rb);
		}
		return group;
	}
}

/// <summary>
/// A simple CardView that uses ControlTemplate for its visual appearance.
/// </summary>
class CardView : ContentView
{
	public static readonly BindableProperty CardTitleProperty =
		BindableProperty.Create(nameof(CardTitle), typeof(string), typeof(CardView), string.Empty);

	public static readonly BindableProperty CardDescriptionProperty =
		BindableProperty.Create(nameof(CardDescription), typeof(string), typeof(CardView), string.Empty);

	public static readonly BindableProperty BorderColorProperty =
		BindableProperty.Create(nameof(BorderColor), typeof(Color), typeof(CardView), Colors.Gray);

	public string CardTitle
	{
		get => (string)GetValue(CardTitleProperty);
		set => SetValue(CardTitleProperty, value);
	}

	public string CardDescription
	{
		get => (string)GetValue(CardDescriptionProperty);
		set => SetValue(CardDescriptionProperty, value);
	}

	public Color BorderColor
	{
		get => (Color)GetValue(BorderColorProperty);
		set => SetValue(BorderColorProperty, value);
	}

	public CardView()
	{
		ControlTemplate = new ControlTemplate(() =>
		{
			var border = new Border
			{
				StrokeThickness = 2,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
				Padding = 16,
			};
			border.SetBinding(Border.StrokeProperty, new Binding(nameof(BorderColor), source: RelativeBindingSource.TemplatedParent));

			var titleLabel = new Label
			{
				FontSize = 18,
				FontAttributes = FontAttributes.Bold,
			};
			titleLabel.SetBinding(Label.TextProperty, new Binding(nameof(CardTitle), source: RelativeBindingSource.TemplatedParent));

			var descLabel = new Label { FontSize = 14 };
			descLabel.SetBinding(Label.TextProperty, new Binding(nameof(CardDescription), source: RelativeBindingSource.TemplatedParent));

			var separator = new BoxView
			{
				HeightRequest = 1,
				Color = Colors.Gray,
				Opacity = 0.3,
			};

			var content = new VerticalStackLayout
			{
				Spacing = 8,
				Children = { titleLabel, separator, descLabel, new ContentPresenter() }
			};

			border.Content = content;
			return border;
		});
	}
}
