using EssentialsAISample.Pages;

namespace EssentialsAISample;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		Routing.RegisterRoute(nameof(LandmarkDetailPage), typeof(LandmarkDetailPage));
		Routing.RegisterRoute(nameof(TripPlanningPage), typeof(TripPlanningPage));
	}
}
