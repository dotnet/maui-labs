namespace CometAllTheLists.Pages;

public class StreamingServicePageState
{
	public List<string> RecommendedShows { get; set; } = new List<string>
	{
		"Breaking Bad", "The Crown", "Stranger Things", "The Mandalorian", "Succession", "Ozark"
	};
	public List<string> NewShows { get; set; } = new List<string>
	{
		"Wednesday", "The Last of Us", "Beef", "The Bear", "Killers of the Flower Moon", "Emerald"
	};
	public List<string> ActionShows { get; set; } = new List<string>
	{
		"John Wick", "Mission Impossible", "Fast & Furious", "James Bond", "Black Panther", "Dune"
	};
	public List<string> DramaShows { get; set; } = new List<string>
	{
		"Parasite", "Oppenheimer", "Barbie", "Poor Things", "Killers of the Flower Moon", "Anatomy of a Fall"
	};
	public List<string> ComedyShows { get; set; } = new List<string>
	{
		"The Office", "Parks and Recreation", "Brooklyn Nine-Nine", "Schitt's Creek", "Always Sunny", "It's Always Sunny"
	};
}

public class StreamingServicePage : Component<StreamingServicePageState>
{
	public override View Render()
	{
		return VStack(spacing: 16,
			Text("Streaming Service")
				.FontSize(24)
				.FontWeight(FontWeight.Bold)
				.Padding(16),

			RenderCollectionSection("Recommended For You", State.RecommendedShows),
			RenderCollectionSection("Newly Added", State.NewShows),
			RenderCollectionSection("Action", State.ActionShows),
			RenderCollectionSection("Drama", State.DramaShows),
			RenderCollectionSection("Comedy", State.ComedyShows)
		)
		.Padding(8);
	}

	View RenderCollectionSection(string title, List<string> shows)
	{
		return new Grid(
			rows: new object[] { "Auto", "*" },
			columns: new object[] { "*" })
		{
			Text(title)
				.FontSize(16)
				.FontWeight(FontWeight.Bold)
				.Padding(new Thickness(12, 0))
				.Cell(row: 0, column: 0),

			new CollectionView<string>(() => shows)
			{
				ItemsLayout = ItemsLayout.Horizontal(spacing: 12),
				ViewFor = show => RenderShowCard(show),
			}.Frame(height: 140)
			 .Cell(row: 1, column: 0),
		};
	}

	View RenderShowCard(string showName)
	{
		return VStack(spacing: 4,
			new ShapeView(new RoundedRectangle(cornerRadius: 6))
				.Frame(width: 100, height: 100)
				.Background(new SolidPaint(GetRandomColor())),
			Text(showName)
				.FontSize(10)
		)
		.Padding(4);
	}

	Color GetRandomColor()
	{
		var colors = new[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Purple, Colors.Orange, Colors.Teal };
		return colors[DateTime.Now.Millisecond % colors.Length];
	}
}
