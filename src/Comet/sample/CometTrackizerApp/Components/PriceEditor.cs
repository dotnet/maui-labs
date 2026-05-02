namespace CometTrackizerApp.Components;

public class PriceEditor : View
{
	[Body]
	View body() =>
		new Grid(
			rows: new object[] { "*" },
			columns: new object[] { "Auto", "*", "Auto" })
		{
			OperationButton("plus.png").Cell(row: 0, column: 0),

			VStack(8,
				TrackizerTheme.H1("Monthly price")
					.Color(TrackizerTheme.Grey40)
					.HorizontalTextAlignment(TextAlignment.Center),
				TrackizerTheme.H4("$5.99")
					.Color(TrackizerTheme.White)
					.FontWeight(FontWeight.Bold)
					.HorizontalTextAlignment(TextAlignment.Center),
				Border(Spacer())
					.Frame(height: 1, width: 170)
					.Background(TrackizerTheme.Grey70)
			)
			.Alignment(Alignment.Center)
			.Cell(row: 0, column: 1),

			OperationButton("minus.png").Cell(row: 0, column: 2),
		};

	static View OperationButton(string imageSource) =>
		Border(
			Image(imageSource).Frame(width: 48, height: 48)
		)
		.Background(TrackizerTheme.Grey60)
		.ClipShape(new RoundedRectangle(16))
		.Alignment(Alignment.Center);
}
