using CometTrackizerApp.Models;

namespace CometTrackizerApp.Views;

public class CalendarViewState
{
	public int SelectedMonth { get; set; } = DateTime.Now.Month;

	public List<Subscription> Subscriptions { get; set; } =
	[
		new(SubscriptionType.Spotify, 5.99, DateOnly.FromDateTime(DateTime.Now)),
		new(SubscriptionType.YouTube, 18.99, DateOnly.FromDateTime(DateTime.Now.AddDays(-2))),
		new(SubscriptionType.OneDrive, 29.99, DateOnly.FromDateTime(DateTime.Now.AddDays(-5))),
		new(SubscriptionType.Netflix, 9.99, DateOnly.FromDateTime(DateTime.Now.AddDays(-7)))
	];
}

public class CalendarView : Component<CalendarViewState>
{
	public override View Render() =>
		new Grid(
			rows: new object[] { "Auto", "Auto", "*" },
			columns: new object[] { "*" })
		{
			// Top calendar section
			TopCalendar().Cell(row: 0, column: 0),

			// Month summary
			new Grid(
				rows: new object[] { "Auto", "Auto" },
				columns: new object[] { "*", "*" })
			{
				TrackizerTheme.H4(new DateTime(DateTime.Now.Year, State.SelectedMonth, 1).ToString("MMMM"))
					.Color(TrackizerTheme.White)
					.Cell(row: 0, column: 0),

				TrackizerTheme.H4("$24.99")
					.Color(TrackizerTheme.White)
					.FontWeight(FontWeight.Bold)
					.Cell(row: 0, column: 1),

				TrackizerTheme.H1(new DateTime(DateTime.Now.Year, State.SelectedMonth, 1).ToString("dd.MM.yyyy"))
					.Color(TrackizerTheme.Grey30)
					.Cell(row: 1, column: 0),

				TrackizerTheme.H1("in upcoming bills")
					.Color(TrackizerTheme.Grey30)
					.Cell(row: 1, column: 1),
			}
			.Margin(new Thickness(24, 16))
			.Cell(row: 1, column: 0),

			// Subscription grid
			ScrollView(Orientation.Vertical,
				VStack(8,
					State.Subscriptions.Select(sub => SubscriptionCard(sub)).ToArray()
				)
				.Margin(new Thickness(24, 0, 24, 80))
			).Cell(row: 2, column: 0),
		};

	View TopCalendar() =>
		Border(
			VStack(16,
				TrackizerTheme.H7("Subs Schedule")
					.Color(TrackizerTheme.White)
					.FontWeight(FontWeight.Bold)
					.Margin(new Thickness(24, 48, 0, 0)),

				TrackizerTheme.H2("3 subscriptions for today")
					.Color(TrackizerTheme.White)
					.Margin(new Thickness(24, 0)),

				// Calendar day strip (simplified)
				ScrollView(Orientation.Horizontal,
					HStack(8,
						Enumerable.Range(1, 28).Select(day =>
							CalendarDayCell(day, day == DateTime.Now.Day, day == 8)
						).ToArray()
					)
					.Margin(new Thickness(24, 0))
				).Frame(height: 105)
			)
		)
		.Background(TrackizerTheme.Grey70)
		.ClipShape(new RoundedRectangle(24));

	View CalendarDayCell(int day, bool isCurrent, bool hasSubscriptions) =>
		Border(
			VStack(
				TrackizerTheme.H4(day.ToString("00"))
					.Color(TrackizerTheme.White)
					.FontWeight(FontWeight.Bold)
					.HorizontalTextAlignment(TextAlignment.Center)
					.Margin(new Thickness(10, 8, 10, 0)),

				TrackizerTheme.H1(
					new DateTime(DateTime.Now.Year, State.SelectedMonth,
						Math.Min(day, DateTime.DaysInMonth(DateTime.Now.Year, State.SelectedMonth)))
					.ToString("ddd")[..2])
					.Color(TrackizerTheme.Grey30)
					.HorizontalTextAlignment(TextAlignment.Center),

				hasSubscriptions
					? Border(Spacer())
						.Frame(width: 6, height: 6)
						.Background(TrackizerTheme.Accentp100)
						.ClipShape(new Ellipse())
						.Alignment(Alignment.Center)
						.Margin(new Thickness(0, 8))
					: Spacer().Frame(height: 6)
			)
		)
		.Background(isCurrent ? TrackizerTheme.Grey60 : TrackizerTheme.Grey60.WithAlpha(0.2f))
		.RoundedBorder(radius: 16, color: TrackizerTheme.Grey60.WithAlpha(0.5f), strokeSize: 0.5f)
		.Frame(width: 48);

	View SubscriptionCard(Subscription sub) =>
		Border(
			new Grid(
				rows: new object[] { "*", "*" },
				columns: new object[] { "*" })
			{
				Image($"{sub.Type.ToString().ToLower()}.png")
					.Frame(width: 40, height: 40)
					.Alignment(Alignment.TopLeading)
					.Margin(new Thickness(16))
					.Cell(row: 0, column: 0),

				VStack(5,
					TrackizerTheme.H2(sub.Type.GetDisplayName())
						.Color(TrackizerTheme.White),
					TrackizerTheme.H4($"${sub.MonthBill}")
						.Color(TrackizerTheme.White)
						.FontWeight(FontWeight.Bold)
				)
				.Alignment(Alignment.BottomLeading)
				.Margin(new Thickness(16))
				.Cell(row: 1, column: 0),
			}
		)
		.Background(TrackizerTheme.Grey60.WithAlpha(0.2f))
		.RoundedBorder(radius: 16, color: TrackizerTheme.Grey60.WithAlpha(0.5f), strokeSize: 0.5f)
		.Frame(height: 168);
}
