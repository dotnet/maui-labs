namespace CometAllTheLists.Pages;

public class CollectionViewPageState
{
	public List<string> Items { get; set; } =
		Enumerable.Range(1, 30).Select(i => $"Item {i}").ToList();
}

public class CollectionViewPage : Component<CollectionViewPageState>
{
	public override View Render()
	{
		return new Grid(
			rows: new object[] { "Auto", "*" },
			columns: new object[] { "*" })
		{
			Text("Collections")
				.FontSize(24)
				.FontWeight(FontWeight.Bold)
				.Padding(16)
				.Cell(row: 0, column: 0),

			VStack(spacing: 16,
				Text("Vertical Collection")
					.FontSize(16)
					.FontWeight(FontWeight.Bold)
					.Padding(new Thickness(12, 0)),

				new CollectionView<string>(() => State.Items)
				{
					ViewFor = item => RenderVerticalItem(item),
					ItemsLayout = ItemsLayout.Vertical(spacing: 8),
				}.Frame(height: 250),

				Text("Horizontal Collection")
					.FontSize(16)
					.FontWeight(FontWeight.Bold)
					.Padding(new Thickness(12, 0)),

				new CollectionView<string>(() => State.Items.Take(10).ToList())
				{
					ViewFor = item => RenderHorizontalItem(item),
					ItemsLayout = ItemsLayout.Horizontal(spacing: 8),
				}.Frame(height: 120),

				Text("Grid Collection (2 columns)")
					.FontSize(16)
					.FontWeight(FontWeight.Bold)
					.Padding(new Thickness(12, 0)),

				new CollectionView<string>(() => State.Items.Take(12).ToList())
				{
					ViewFor = item => RenderGridItem(item),
					ItemsLayout = GridItemsLayout.Vertical(span: 2, spacing: 8),
				}.Frame(height: 280)
			)
			.Padding(8)
			.Cell(row: 1, column: 0),
		};
	}

	View RenderVerticalItem(string item)
	{
		return VStack(spacing: 4,
			HStack(spacing: 8,
				new ShapeView(new Circle())
					.Frame(width: 10, height: 10)
					.Background(new SolidPaint(Colors.Blue)),
				Text(item).FontSize(14),
				Spacer(),
				Text("→").FontSize(12).Color(Colors.Gray)
			)
		)
		.Padding(12)
		.Background(new SolidPaint(Color.FromArgb("#F5F5F5")));
	}

	View RenderHorizontalItem(string item)
	{
		return VStack(spacing: 4,
			new ShapeView(new Circle())
				.Frame(width: 30, height: 30)
				.Background(new SolidPaint(Colors.Cyan)),
			Text(item).FontSize(10)
		)
		.Padding(8)
		.Background(new SolidPaint(Colors.White));
	}

	View RenderGridItem(string item)
	{
		return VStack(
			new ShapeView(new Rectangle())
				.Frame(height: 100)
				.Background(new SolidPaint(Colors.Purple)),
			Text(item)
				.FontSize(12)
				.Padding(6)
		)
		.Background(new SolidPaint(Colors.White));
	}
}
