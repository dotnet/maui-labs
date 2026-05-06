namespace CometAllTheLists.Pages;

public class Product
{
	public string Name { get; set; } = "";
	public string Category { get; set; } = "";
	public decimal Price { get; set; }
	public bool IsNew { get; set; }
	public int Quantity { get; set; }
	public bool IsLoading { get; set; }
}

public class ShoppingPageState
{
	public List<Product> Products { get; set; } = new List<Product>
	{
		new() { Name = "Laptop", Category = "Electronics", Price = 999, IsNew = true, Quantity = 5 },
		new() { Name = "Mouse", Category = "Accessories", Price = 29, IsNew = false, Quantity = 50 },
		new() { Name = "Keyboard", Category = "Accessories", Price = 79, IsNew = true, Quantity = 25 },
		new() { Name = "Monitor", Category = "Electronics", Price = 299, IsNew = false, Quantity = 10 },
		new() { Name = "USB Cable", Category = "Cables", Price = 9, IsNew = false, Quantity = 100 },
		new() { Name = "Headphones", Category = "Audio", Price = 149, IsNew = true, Quantity = 15 },
		new() { Name = "Webcam", Category = "Electronics", Price = 79, IsNew = false, Quantity = 20 },
		new() { Name = "Desk Lamp", Category = "Furniture", Price = 59, IsNew = true, Quantity = 8 },
	};
}

public class ShoppingPage : Component<ShoppingPageState>
{
	public override View Render()
	{
		return new Grid(
			rows: new object[] { "Auto", "*" },
			columns: new object[] { "*" })
		{
			Text("Shopping Products")
				.FontSize(24)
				.FontWeight(FontWeight.Bold)
				.Padding(16)
				.Cell(row: 0, column: 0),
			new CollectionView<Product>(() => State.Products)
			{
				ViewFor = item => RenderProductItem(item),
				ItemsLayout = ItemsLayout.Vertical(spacing: 8),
			}.Padding(8)
			 .Cell(row: 1, column: 0),
		};
	}

	View RenderProductItem(Product item)
	{
		if (item.IsLoading)
		{
			return VStack(
				Text("Loading more...")
			)
			.Padding(16)
			.Background(new SolidPaint(Colors.LightGray));
		}

		if (item.Price > 100)
		{
			return RenderPremiumItem(item);
		}

		return RenderStandardItem(item);
	}

	View RenderPremiumItem(Product item)
	{
		return VStack(spacing: 6,
			HStack(spacing: 12,
				VStack(spacing: 4,
					Text(item.Name)
						.FontSize(16)
						.FontWeight(FontWeight.Bold),
					Text(item.Category)
						.FontSize(12)
						.Color(Colors.Gray)
				),
				Spacer(),
				VStack(LayoutAlignment.End, spacing: 2,
					Text($"${item.Price}")
						.FontSize(18)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.Green),
					item.IsNew ? Text("NEW")
						.FontSize(10)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.White)
						.Padding(new Thickness(4, 2))
						.Background(new SolidPaint(Colors.Orange))
						: (View)Text("")
				)
			),
			Text($"In stock: {item.Quantity} units")
				.FontSize(11)
				.Color(Colors.Gray)
		)
		.Padding(12)
		.Background(new SolidPaint(Color.FromArgb("#F0F0F0")));
	}

	View RenderStandardItem(Product item)
	{
		return HStack(spacing: 12,
			VStack(spacing: 4,
				Text(item.Name)
					.FontSize(14)
					.FontWeight(FontWeight.Bold),
				Text($"${item.Price} • {item.Quantity} in stock")
					.FontSize(11)
					.Color(Colors.Gray)
			),
			Spacer(),
			Text($"${item.Price}")
				.FontSize(16)
				.FontWeight(FontWeight.Bold)
		)
		.Padding(10)
		.Background(new SolidPaint(Colors.White));
	}
}
