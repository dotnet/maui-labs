using CometOrderingApp.Models;

namespace CometOrderingApp.Pages;

// ── State Classes ─────────────────────────────────────────────

class MainPageState
{
	public ProductType SelectedType { get; set; }
	public int? SelectedItemIndex { get; set; }
}

class CartState
{
	public double SizeCost { get; set; } = 12;
	public int SelectedCrust { get; set; } = 2; // 0=Hand Tossed, 1=Thin, 2=Cheese Burst
	public double AddOn1Cost { get; set; }
	public double AddOn2Cost { get; set; }

	public double CrustCost => SelectedCrust == 2 ? 1.5 : 0;
}

// ── Main Page ─────────────────────────────────────────────────

class MainPage : Component<MainPageState>
{
	public override View Render()
	{
		// Filter products by selected category
		var filteredItems = ProductItem.Items
			.Where(p => p.Type == State.SelectedType)
			.ToArray();

		return new Grid(
			rows: new object[] { 290, "*" },
			columns: new object[] { "*" })
		{
			new HeaderView(
				State.SelectedType,
				type => SetState(s => s.SelectedType = type)),

			ScrollView(
				VStack(spacing: 0,
					filteredItems.Select(RenderProductItem).ToArray()
				)
				.Margin(left: 24, top: 20, right: 24, bottom: 20)
			)
			.GridRow(1),

			State.SelectedItemIndex.HasValue
				? new CartPanel(
					ProductItem.Items[State.SelectedItemIndex.Value],
					() => SetState(s => s.SelectedItemIndex = null))
					.GridRowSpan(2)
				: (View)new Spacer()
					.IsVisible(false)
					.GridRowSpan(2)
		};
	}

	View RenderProductItem(ProductItem item)
	{
		return Border(
			new Grid(
				rows: new object[] { "*" },
				columns: new object[] { 100, "*" })
			{
				Image($"{item.Image}.png")
					.Frame(width: 100, height: 100)
					.Margin(left: 8)
					.Alignment(Comet.Alignment.Center),

				new Grid(
					rows: new object[] { 20, "*", 24 },
					columns: new object[] { "*" })
				{
					Text(item.Title)
						.FontFamily("MulishSemiBold")
						.FontSize(18)
						.Color(Colors.Black),

					Text(item.Description)
						.FontFamily("MulishRegular")
						.FontSize(12)
						.Color(Color.FromArgb("#121212"))
						.GridRow(1),

					Text($"${item.Cost}")
						.FontFamily("MulishSemiBold")
						.FontSize(16)
						.Color(Colors.Black)
						.GridRow(2)
				}
				.GridColumn(1)
				.Margin(left: 12, top: 15, right: 12, bottom: 15),

				Border(
					Text("+ ADD")
						.FontFamily("MulishBold")
						.Color(Colors.White)
						.VerticalTextAlignment(TextAlignment.Center)
						.HorizontalTextAlignment(TextAlignment.Center)
				)
				.OnTap(_ => SetState(s => s.SelectedItemIndex = ProductItem.Items.IndexOf(item)))
				.Frame(width: 78, height: 34)
				.Background(Theme.PrimaryColor)
				.CornerRadius(13, 0, 0, 13)
				.GridColumn(1)
				.Alignment(Comet.Alignment.BottomTrailing)
			}
		)
		.CornerRadius(13)
		.Background(new LinearGradientPaint(
			new PaintGradientStop[]
			{
				new PaintGradientStop(0.0537f, Theme.PrimaryLightColor),
				new PaintGradientStop(0.9738f, Colors.White)
			},
			startPoint: new Point(0, 0.5),
			endPoint: new Point(1, 0.5)))
		.Margin(bottom: 12)
		.Frame(height: 132);
	}
}

// ── Header ────────────────────────────────────────────────────

class HeaderView : View
{
	readonly ProductType _selectedType;
	readonly Action<ProductType> _onTypeSelected;

	public HeaderView(ProductType selectedType, Action<ProductType> onTypeSelected)
	{
		_selectedType = selectedType;
		_onTypeSelected = onTypeSelected;
	}

	[Body]
	View body()
	{
		return new Grid(
			rows: new object[] { 92, 49, 130 },
			columns: new object[] { "*" })
		{
			// Top bar row
			new Grid(
				rows: new object[] { 30 },
				columns: new object[] { 39, 30, "*", 110 })
			{
				Image("menu_icon.png")
					.Margin(right: 12, top: 3, bottom: 3)
					.Aspect(Aspect.AspectFit),

				Image("dodo.png")
					.GridColumn(1),

				Image("title.png")
					.GridColumn(2)
					.Margin(left: 12, right: 12),

				HStack(spacing: 2,
					Text("DELIVERY")
						.Color(Theme.PrimaryColor)
						.VerticalTextAlignment(TextAlignment.Center),
					Image("chevron_down.png")
				)
				.GridColumn(3)
				.Alignment(Comet.Alignment.Trailing)
			}
			.Margin(left: 24, top: 62, right: 24),

			// Address bar row
			Border(
				new Grid(
					rows: new object[] { 32 },
					columns: new object[] { "*" })
				{
					Text("29 Hola street, California, USA")
						.Margin(left: 8, right: 8)
						.FontSize(14)
						.FontFamily("MulishSemiBold")
						.Color(Theme.PrimaryColor)
						.VerticalTextAlignment(TextAlignment.Center),

					Image("pin.png")
						.Frame(height: 16)
						.Margin(left: 8, right: 8)
						.Alignment(Comet.Alignment.Trailing)
				}
			)
			.Background(Theme.PrimaryColor.WithAlpha(0.1f))
			.CornerRadius(4)
			.StrokeThickness(0)
			.GridRow(1)
			.Margin(left: 24, top: 16, right: 24),

			// Category selector row
			ScrollView(
				Orientation.Horizontal,
				HStack(spacing: 10,
					RenderProductTypes()
				)
			)
			.Margin(top: 20)
			.Padding(new Thickness(24, 0, 24, 4))
			.GridRow(2)
		}
		.Background(Theme.PrimaryLightColor);
	}

	View[] RenderProductTypes()
	{
		return Enum.GetValues<ProductType>()
			.Select(RenderProductType)
			.ToArray();
	}

	View RenderProductType(ProductType type)
	{
		var isSelected = type == _selectedType;

		return Border(
			new Grid(
				rows: new object[] { "*", 20 },
				columns: new object[] { "*" })
			{
				Image($"{type.ToString().ToLowerInvariant()}.png")
					.Frame(width: 64, height: 64)
					.Alignment(Alignment.Center),

				Text(type.ToString())
					.FontFamily("MulishSemiBold")
					.Color(Colors.Black)
					.FontSize(14)
					.GridRow(1)
					.HorizontalTextAlignment(TextAlignment.Center)
					.VerticalTextAlignment(TextAlignment.Center)
			}
			.Padding(new Thickness(8))
			.Background(Theme.PrimaryLightColor)
		)
		.Frame(width: 80)
		.OnTap(_ => _onTypeSelected?.Invoke(type))
		.Background(Theme.PrimaryLightColor)
		.CornerRadius(12)
		.StrokeColor(isSelected ? Theme.PrimaryColor : Colors.Transparent)
		.StrokeThickness(isSelected ? 2 : 0);
	}
}

// ── Cart Panel ────────────────────────────────────────────────

class CartPanel : Component<CartState>
{
	readonly ProductItem _item;
	readonly Action _onClose;

	public CartPanel(ProductItem item, Action onClose)
	{
		_item = item;
		_onClose = onClose;
	}

	public override View Render()
	{
		return new Grid(
			rows: new object[] { "*" },
			columns: new object[] { "*" })
		{
			// Dim overlay
			new BoxView(Colors.Black.WithAlpha(0.8f)),

			// Cart content
			new Grid(
				rows: new object[] { "*", 78 },
				columns: new object[] { "*" })
			{
				RenderBody(),
				RenderBottom().GridRow(1)
			}
			.Frame(height: 600)
			.Alignment(Alignment.Bottom)
			.Background(Colors.White)
		};
	}

	View RenderBody()
	{
		return new Grid(
			rows: new object[] { 66, "*" },
			columns: new object[] { "*" })
		{
			// Title + close button
			new Grid(
				rows: new object[] { "*" },
				columns: new object[] { "*", 24 })
			{
				Text(_item.Title)
					.FontFamily("MulishBold")
					.FontSize(18)
					.Color(Colors.Black),

				Image("close.png")
					.GridColumn(1)
					.OnTap(_ => _onClose?.Invoke())
			}
			.Margin(left: 24, top: 20, right: 24, bottom: 20),

			// Options
			ScrollView(
				VStack(spacing: 20,
					RenderSizeGroup(),
					RenderCrustGroup(),
					RenderAddOnsGroup()
				)
				.Margin(left: 24, right: 24)
			)
			.GridRow(1)
		};
	}

	View RenderSizeGroup()
	{
		return RenderCartItemGroup("Choose Size", false,
			RenderCartItem("Small - 6''", 8, State.SizeCost == 8,
				() => SetState(s => s.SizeCost = 8)),
			RenderCartItem("Medium - 10''", 12, State.SizeCost == 12,
				() => SetState(s => s.SizeCost = 12)),
			RenderCartItem("Large - 14''", 16, State.SizeCost == 16,
				() => SetState(s => s.SizeCost = 16))
		);
	}

	View RenderCrustGroup()
	{
		return RenderCartItemGroup("Select Crust", false,
			RenderCartItem("Classic Hand tossed", 0, State.SelectedCrust == 0,
				() => SetState(s => s.SelectedCrust = 0)),
			RenderCartItem("Thin Crust", 0, State.SelectedCrust == 1,
				() => SetState(s => s.SelectedCrust = 1)),
			RenderCartItem("Cheese Brust", 1.5, State.SelectedCrust == 2,
				() => SetState(s => s.SelectedCrust = 2))
		);
	}

	View RenderAddOnsGroup()
	{
		return RenderCartItemGroup("Add ons", true,
			RenderCartItem("Add Extra Cheese", 2.5, State.AddOn1Cost == 2.5,
				() => SetState(s => s.AddOn1Cost = s.AddOn1Cost == 0 ? 2.5 : 0), checkBox: true),
			RenderCartItem("Add Mushroom", 2.5, State.AddOn2Cost == 2.5,
				() => SetState(s => s.AddOn2Cost = s.AddOn2Cost == 0 ? 2.5 : 0), checkBox: true)
		);
	}

	static View RenderCartItemGroup(string title, bool optional, params View[] items)
	{
		var children = new List<View>();
		for (int i = 0; i < items.Length; i++)
		{
			children.Add(items[i]);
			if (i < items.Length - 1)
			{
				children.Add(
					new BoxView(Color.FromArgb("#E6E6E6"))
						.Frame(height: 2)
						.Margin(left: 32)
				);
			}
		}

		return new Grid(
			rows: new object[] { 22, "Auto" },
			columns: new object[] { "*" })
		{
			new Grid(
				rows: new object[] { 22 },
				columns: new object[] { "*", 76 })
			{
				Text(title)
					.FontFamily("MulishBold")
					.FontSize(14)
					.Color(Colors.Black),

				Image(optional ? "optional.png" : "required.png")
					.Aspect(Aspect.AspectFit)
					.GridColumn(1)
			},

			VStack(spacing: 0, children.ToArray())
				.GridRow(1)
		};
	}

	static View RenderCartItem(
		string label,
		double cost,
		bool selected,
		Action onSelected,
		bool checkBox = false)
	{
		return new Grid(
			rows: new object[] { 40 },
			columns: new object[] { 24, "*", 30 })
		{
			RenderIndicator(selected, checkBox),

			Text(label)
				.FontFamily("MulishRegular")
				.FontSize(12)
				.Color(Colors.Black)
				.GridColumn(1)
				.VerticalTextAlignment(TextAlignment.Center)
				.Margin(left: 9, right: 9),

			cost > 0
				? Text($"${cost}")
					.FontFamily("MulishRegular")
					.FontSize(12)
					.Color(Colors.Black)
					.VerticalTextAlignment(TextAlignment.Center)
					.GridColumn(2)
				: (View)new Spacer().GridColumn(2)
		}
		.OnTap(_ => onSelected?.Invoke());
	}

	static View RenderIndicator(bool selected, bool isCheckBox)
	{
		var fillColor = selected ? Theme.PrimaryColor : Colors.Transparent;
		var borderColor = selected ? Theme.PrimaryColor : Color.FromArgb("#CCCCCC");

		if (isCheckBox)
		{
			return Border(
				new BoxView(fillColor)
					.Frame(width: 10, height: 10)
					.Alignment(Alignment.Center)
			)
			.Frame(width: 24, height: 24)
			.Alignment(Alignment.Center)
			.CornerRadius(4)
			.StrokeColor(borderColor)
			.StrokeThickness(2)
			.Background(Colors.Transparent);
		}

		// Radio indicator
		return Border(
			new BoxView(fillColor)
				.Frame(width: 10, height: 10)
				.Alignment(Alignment.Center)
				.ClipShape(new RoundedRectangle(5))
		)
		.Frame(width: 24, height: 24)
		.Alignment(Alignment.Center)
		.CornerRadius(12)
		.StrokeColor(borderColor)
		.StrokeThickness(2)
		.Background(Colors.Transparent);
	}

	View RenderBottom()
	{
		var total = State.SizeCost + State.CrustCost + State.AddOn1Cost + State.AddOn2Cost;

		return new Grid(
			rows: new object[] { "*" },
			columns: new object[] { "*", 120 })
		{
			// Separator line
			new BoxView(Color.FromArgb("#E6E6E6"))
				.Frame(height: 2)
				.GridColumnSpan(2)
				.Alignment(Alignment.Top),

			Button("+ ADD TO CART", () => _onClose?.Invoke())
				.FontSize(14)
				.FontFamily("MulishSemiBold")
				.Color(Colors.White)
				.Background(Theme.PrimaryColor)
				.CornerRadius(8)
				.FillHorizontal(),

			Text($"{total:$0.00}")
				.FontFamily("MulishSemiBold")
				.FontSize(16)
				.Color(Colors.Black)
				.FontWeight(FontWeight.Bold)
				.Alignment(Comet.Alignment.Trailing)
				.GridColumn(1)
		}
		.Margin(left: 24, top: 20, right: 24, bottom: 20);
	}
}
