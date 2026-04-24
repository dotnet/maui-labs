using CometTrackizerApp.Components;
using CometTrackizerApp.Models;
using CometTrackizerApp.Views;

namespace CometTrackizerApp.Pages;

public enum HomeScreenView
{
	[Display(Name = "")]
	Home,

	[Display(Name = "Spending & Budgets")]
	Budgets,

	[Display(Name = "Calendar")]
	Calendar,

	[Display(Name = "Credit Cards")]
	CreditCards
}

public class HomeScreen : View
{
	readonly HomeScreenView _homeView;
	readonly Action<HomeScreenView> _onViewChanged;
	readonly Action _onNewSubscription;
	readonly Action _onShowBudgets;

	public HomeScreen(
		HomeScreenView homeView,
		Action<HomeScreenView> onViewChanged,
		Action onNewSubscription,
		Action onShowBudgets)
	{
		_homeView = homeView;
		_onViewChanged = onViewChanged;
		_onNewSubscription = onNewSubscription;
		_onShowBudgets = onShowBudgets;
	}

	[Body]
	View body() => new ZStack
	{
		// Current view content
		RenderView(),

		// Title bar
		TrackizerTheme.H3(_homeView.GetDisplayName())
			.Color(TrackizerTheme.Grey30)
			.HorizontalTextAlignment(TextAlignment.Center)
			.Alignment(Alignment.Top)
			.Margin(new Thickness(23, 32, 23, 0)),

		// Settings icon
		Image("settings_dark.png")
			.Frame(width: 24, height: 24)
			.Alignment(Alignment.TopTrailing)
			.Margin(new Thickness(0, 32, 23, 0)),

		// Bottom navigation bar
		new NavigationBar(_homeView, _onViewChanged, _onNewSubscription),
	}
	.Background(TrackizerTheme.Grey80);

	View RenderView() => _homeView switch
	{
		HomeScreenView.Budgets => new BudgetsView(),
		HomeScreenView.Calendar => new CalendarView(),
		HomeScreenView.CreditCards => new CreditCardsView(),
		_ => new HomeView(_onShowBudgets),
	};
}
