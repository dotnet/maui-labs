using CometTrackizerApp.Models;

namespace CometTrackizerApp.Views;

public enum HomeViewListType
{
	Subscriptions,
	UpcomingBills
}

public class HomeViewState
{
	public HomeViewListType ListType { get; set; }

	public List<Subscription> Subscriptions { get; set; } =
	[
		new(SubscriptionType.Spotify, 5.99, DateOnly.FromDateTime(DateTime.Now)),
		new(SubscriptionType.YouTube, 18.99, DateOnly.FromDateTime(DateTime.Now.AddDays(-2))),
		new(SubscriptionType.OneDrive, 29.99, DateOnly.FromDateTime(DateTime.Now.AddDays(-5))),
		new(SubscriptionType.Netflix, 9.99, DateOnly.FromDateTime(DateTime.Now.AddDays(-7)))
	];
}

public class HomeView : Component<HomeViewState>
{
	readonly Action _onShowBudgetView;

	public HomeView(Action onShowBudgetView)
	{
		_onShowBudgetView = onShowBudgetView;
	}

	public override View Render() =>
		new Grid(
			rows: new object[] { "Auto", "*" },
			columns: new object[] { "*" })
		{
			VStack(
				BudgetIndicator(),
				ListTypeTab()
			).Cell(row: 0, column: 0),

			new ZStack
			{
				State.ListType == HomeViewListType.Subscriptions
					? SubscriptionList()
					: UpcomingBillsList(),

				// Fade overlay at bottom
				Border(Spacer())
					.Alignment(Alignment.Bottom)
					.Frame(height: 90)
					.Background(TrackizerTheme.Grey80.WithAlpha(0.7f))
			}.Cell(row: 1, column: 0)
		};

	View BudgetIndicator() =>
		Border(
			VStack(16,
				Image("full_logo.png")
					.Frame(width: 107)
					.Alignment(Alignment.Center),

				TrackizerTheme.H7("$1,235")
					.Color(TrackizerTheme.White)
					.HorizontalTextAlignment(TextAlignment.Center),

				TrackizerTheme.H1("This month bills")
					.Color(TrackizerTheme.Grey40)
					.HorizontalTextAlignment(TextAlignment.Center),

				TrackizerTheme.ThemedButton("See your budget", _onShowBudgetView)
					.Margin(new Thickness(60, 0)),

				// Budget summary row
				HStack(8,
					BudgetItem(TrackizerTheme.Accentp100, "Active subs", "12"),
					BudgetItem(TrackizerTheme.Primary100, "Highest subs", "$19.99"),
					BudgetItem(TrackizerTheme.Accents50, "Lowest subs", "$5.99")
				)
			)
			.Padding(new Thickness(24))
		)
		.Background(TrackizerTheme.Grey70)
		.ClipShape(new RoundedRectangle(24));

	View BudgetItem(Color topColor, string topText, string bottomText) =>
		Border(
			VStack(4,
				Border(Spacer()).Background(topColor).Frame(height: 1).Margin(new Thickness(20, 0)),
				TrackizerTheme.H1(topText)
					.Color(TrackizerTheme.Grey40)
					.HorizontalTextAlignment(TextAlignment.Center),
				TrackizerTheme.H2(bottomText)
					.Color(TrackizerTheme.White)
					.HorizontalTextAlignment(TextAlignment.Center)
			)
		)
		.Background(TrackizerTheme.Grey60)
		.ClipShape(new RoundedRectangle(16))
		.Frame(height: 68);

	View ListTypeTab() =>
		new ZStack
		{
			Border(Spacer())
				.Background(TrackizerTheme.Grey100)
				.ClipShape(new RoundedRectangle(16)),

			HStack(
				Text("Your subscriptions")
					.FontFamily("InterRegular").FontSize(12)
					.Color(State.ListType == HomeViewListType.Subscriptions
						? TrackizerTheme.White : TrackizerTheme.Grey30)
					.HorizontalTextAlignment(TextAlignment.Center)
					.OnTap(_ => SetState(s => s.ListType = HomeViewListType.Subscriptions)),

				Text("Upcoming bills")
					.FontFamily("InterRegular").FontSize(12)
					.Color(State.ListType == HomeViewListType.UpcomingBills
						? TrackizerTheme.White : TrackizerTheme.Grey30)
					.HorizontalTextAlignment(TextAlignment.Center)
					.OnTap(_ => SetState(s => s.ListType = HomeViewListType.UpcomingBills))
			)
		}
		.Frame(height: 50)
		.Margin(new Thickness(24, 21, 24, 0));

	View SubscriptionList() =>
		ScrollView(Orientation.Vertical,
			VStack(8,
				State.Subscriptions.Select(sub => SubscriptionRow(sub)).ToArray()
			)
		)
		.Margin(new Thickness(24, 8, 24, 72));

	View UpcomingBillsList() =>
		ScrollView(Orientation.Vertical,
			VStack(8,
				State.Subscriptions.Select(sub => UpcomingBillRow(sub)).ToArray()
			)
		)
		.Margin(new Thickness(24, 8, 24, 72));

	View SubscriptionRow(Subscription sub) =>
		Border(
			new Grid(
				rows: new object[] { "*" },
				columns: new object[] { "64", "*", "Auto" })
			{
				Image($"{sub.Type.ToString().ToLower()}.png")
					.Frame(width: 40, height: 40)
					.Alignment(Alignment.Center)
					.Cell(row: 0, column: 0),

				TrackizerTheme.H2(sub.Type.GetDisplayName())
					.Color(TrackizerTheme.White)
					.Cell(row: 0, column: 1),

				TrackizerTheme.H2($"${sub.MonthBill}")
					.Color(TrackizerTheme.White)
					.Margin(new Thickness(17, 0))
					.Cell(row: 0, column: 2)
			}
		)
		.RoundedBorder(radius: 16, color: TrackizerTheme.Grey60, strokeSize: 1)
		.Frame(height: 64);

	View UpcomingBillRow(Subscription sub) =>
		Border(
			new Grid(
				rows: new object[] { "*" },
				columns: new object[] { "64", "*", "Auto" })
			{
				Border(
					VStack(
						TrackizerTheme.BodyExtraSmall($"{sub.StartingDate:MMM}")
							.Color(TrackizerTheme.Grey30),
						TrackizerTheme.BodyMedium($"{sub.StartingDate:dd}")
							.Color(TrackizerTheme.Grey30)
							.HorizontalTextAlignment(TextAlignment.Center)
					).Alignment(Alignment.Center)
				)
				.Background(TrackizerTheme.Grey70)
				.ClipShape(new RoundedRectangle(12))
				.Margin(new Thickness(12))
				.Cell(row: 0, column: 0),

				TrackizerTheme.H2(sub.Type.GetDisplayName())
					.Color(TrackizerTheme.White)
					.Cell(row: 0, column: 1),

				TrackizerTheme.H2($"${sub.MonthBill}")
					.Color(TrackizerTheme.White)
					.Margin(new Thickness(17, 0))
					.Cell(row: 0, column: 2)
			}
		)
		.RoundedBorder(radius: 16, color: TrackizerTheme.Grey60, strokeSize: 1)
		.Frame(height: 64);
}
