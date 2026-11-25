namespace DevFlow.Sample.TvOS;

public class DetailsPage : ContentPage
{
	public DetailsPage()
	{
		Title = "Details";
		Content = new VerticalStackLayout
		{
			Padding = new Thickness(80, 60),
			Children =
			{
				new Label
				{
					AutomationId = "DetailsLabel",
					Text = "Details page",
					FontSize = 48
				}
			}
		};
	}
}
