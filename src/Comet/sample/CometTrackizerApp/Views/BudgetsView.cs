using CometTrackizerApp.Models;

namespace CometTrackizerApp.Views;

public class BudgetsViewState
{
	public BudgetByCategory[] BudgetByCategories { get; set; } =
	[
		new(Category.AutoTransport, 125.99, 400),
		new(Category.Entertainment, 350.99, 600),
		new(Category.Security, 475.99, 600)
	];
}

public class BudgetsView : Component<BudgetsViewState>
{
	public override View Render() =>
		ScrollView(Orientation.Vertical,
			VStack(16,
				// Budget arc replaced with text-based budget summary
				BudgetSummary(),

				// Status banner
				Border(
					HStack(8,
						TrackizerTheme.H2("Your budgets are on track")
							.Color(TrackizerTheme.White),
						Image("like.png").Frame(width: 20, height: 20)
					).Alignment(Alignment.Center)
				)
				.RoundedBorder(radius: 16, color: TrackizerTheme.Grey60, strokeSize: 1)
				.Frame(height: 60),

				// Category list
				VStack(8,
					State.BudgetByCategories.Select(budget =>
						BudgetCategoryRow(budget)
					).ToArray()
				)
			)
			.Margin(new Thickness(24))
		);

	View BudgetSummary()
	{
		var total = State.BudgetByCategories.Sum(b => b.MonthBills);
		var budget = State.BudgetByCategories.Sum(b => b.MonthBudget);

		return Border(
			VStack(8,
				TrackizerTheme.H5($"${total:F0}")
					.Color(TrackizerTheme.White)
					.HorizontalTextAlignment(TextAlignment.Center),
				TrackizerTheme.H1($"of ${budget:F0} budget")
					.Color(TrackizerTheme.Grey40)
					.HorizontalTextAlignment(TextAlignment.Center),

				// Simplified progress bar instead of canvas arcs
				Border(Spacer())
					.Frame(height: 8)
					.Background(TrackizerTheme.Grey60)
					.ClipShape(new RoundedRectangle(4))
					.Margin(new Thickness(20, 16, 20, 0)),
				new ZStack
				{
					Border(Spacer())
						.Frame(height: 8)
						.Background(TrackizerTheme.Accents100)
						.ClipShape(new RoundedRectangle(4)),
				}
				.Margin(new Thickness(20, 0, 20 + (1.0 - total / budget) * 200, 0))
			)
			.Padding(new Thickness(24))
		)
		.Background(TrackizerTheme.Grey70)
		.ClipShape(new RoundedRectangle(24));
	}

	View BudgetCategoryRow(BudgetByCategory budget) =>
		Border(
			new Grid(
				rows: new object[] { "*", "*", "Auto" },
				columns: new object[] { "Auto", "*", "Auto" })
			{
				Image($"{budget.Category.ToString().ToLower()}.png")
					.Frame(width: 32, height: 32)
					.Margin(new Thickness(16))
					.GridRowSpan(2)
					.Cell(row: 0, column: 0),

				TrackizerTheme.H2(budget.Category.GetDisplayName())
					.Color(TrackizerTheme.White)
					.Cell(row: 0, column: 1),

				TrackizerTheme.H1($"$375 left to spend")
					.Color(TrackizerTheme.Grey30)
					.Cell(row: 1, column: 1),

				TrackizerTheme.H2($"${budget.MonthBills}")
					.Color(TrackizerTheme.White)
					.Margin(new Thickness(16, 0))
					.Cell(row: 0, column: 2),

				TrackizerTheme.H1($"of ${budget.MonthBudget}")
					.Color(TrackizerTheme.Grey30)
					.Margin(new Thickness(16, 0))
					.Cell(row: 1, column: 2),

				// Progress bar
				Border(Spacer())
					.Frame(height: 4)
					.Background(TrackizerTheme.Grey30)
					.ClipShape(new RoundedRectangle(2))
					.Margin(new Thickness(16, 0, 16, 11))
					.GridColumnSpan(3)
					.Cell(row: 2, column: 0),
			}
		)
		.RoundedBorder(radius: 16, color: TrackizerTheme.Grey60.WithAlpha(0.5f), strokeSize: 0.5f)
		.Background(TrackizerTheme.Grey60.WithAlpha(0.2f))
		.Frame(height: 84);
}
