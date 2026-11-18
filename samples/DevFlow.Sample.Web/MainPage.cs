namespace DevFlow.Sample.Web;

public class MainPage : ContentPage
{
	int _count;

	public MainPage()
	{
		Title = "DevFlow Web";

		var counterLabel = new Label
		{
			AutomationId = "CounterLabel",
			Text = "Not pressed yet",
			FontSize = 18
		};

		var counterButton = new Button
		{
			AutomationId = "CounterButton",
			Text = "Press me"
		};
		counterButton.Clicked += (_, _) =>
		{
			_count++;
			counterLabel.Text = $"Pressed {_count} time{(_count == 1 ? "" : "s")}";
		};

		var nameEntry = new Entry
		{
			AutomationId = "NameEntry",
			Placeholder = "Your name"
		};

		var greetingLabel = new Label
		{
			AutomationId = "GreetingLabel",
			FontSize = 18
		};
		nameEntry.TextChanged += (_, e) => greetingLabel.Text = string.IsNullOrEmpty(e.NewTextValue)
			? string.Empty
			: $"Hello, {e.NewTextValue}!";

		var detailsButton = new Button
		{
			AutomationId = "DetailsButton",
			Text = "Open details"
		};
		detailsButton.Clicked += async (_, _) => await Navigation.PushAsync(new DetailsPage());

		Content = new ScrollView
		{
			AutomationId = "MainScroll",
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(24),
				Spacing = 30,
				Children =
				{
					new Label
					{
						AutomationId = "TitleLabel",
						Text = "Hello from the browser",
						FontSize = 32,
						FontAttributes = FontAttributes.Bold
					},
					counterButton,
					counterLabel,
					nameEntry,
					greetingLabel,
					new Switch { AutomationId = "SampleSwitch" },
					detailsButton
				}
			}
		};
	}
}
