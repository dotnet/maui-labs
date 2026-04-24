using CometTrackizerApp.Pages;

namespace CometTrackizerApp.Components;

public class NavigationBar : View
{
	readonly HomeScreenView _view;
	readonly Action<HomeScreenView> _onViewChanged;
	readonly Action _onNewSubscription;

	public NavigationBar(
		HomeScreenView view,
		Action<HomeScreenView> onViewChanged,
		Action onNewSubscription)
	{
		_view = view;
		_onViewChanged = onViewChanged;
		_onNewSubscription = onNewSubscription;
	}

	[Body]
	View body() =>
		new ZStack
		{
			// Bar background
			Border(
				new Grid(
					rows: new object[] { "*" },
					columns: new object[] { "*", "Auto", "*" })
				{
					HStack(
						ViewButton("home.png", HomeScreenView.Home),
						ViewButton("budgets.png", HomeScreenView.Budgets)
					).Cell(row: 0, column: 0),

					// Center spacer for plus button
					Spacer().Frame(width: 64).Cell(row: 0, column: 1),

					HStack(
						ViewButton("calendar.png", HomeScreenView.Calendar),
						ViewButton("credit_cards.png", HomeScreenView.CreditCards)
					).Cell(row: 0, column: 2),
				}
			)
			.Background(TrackizerTheme.Grey60.WithAlpha(0.75f))
			.ClipShape(new RoundedRectangle(24))
			.Margin(new Thickness(23, 0, 23, 11)),

			// Plus button
			Image("home_plus.png")
				.Frame(width: 56, height: 56)
				.Alignment(Alignment.Top)
				.OnTap(_ => _onNewSubscription()),
		}
		.Frame(height: 82)
		.Alignment(Alignment.Bottom);

	View ViewButton(string imageSource, HomeScreenView view) =>
		Image(imageSource)
			.Frame(width: 18, height: 18)
			.Opacity(_view == view ? 1.0 : 0.4)
			.Margin(new Thickness(16, 22))
			.OnTap(_ => _onViewChanged(view));
}
