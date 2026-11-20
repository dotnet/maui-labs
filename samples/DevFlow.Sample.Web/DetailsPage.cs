namespace DevFlow.Sample.Web;

public class DetailsPage : ContentPage
{
	public DetailsPage()
	{
		Title = "Details";
		Content = new VerticalStackLayout
		{
			Padding = new Thickness(24),
			Children =
			{
				new Label
				{
					AutomationId = "DetailsLabel",
					Text = "Details page",
					FontSize = 24
				}
			}
		};
	}
}
