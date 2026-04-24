namespace CometBaristaNotes.Pages;

public class EquipmentManagementPageState
{
	public List<Equipment> Equipment { get; set; } = new();
	public bool IsLoaded { get; set; }
}

public class EquipmentManagementPage : Component<EquipmentManagementPageState>
{
	readonly IDataStore _store;

	public EquipmentManagementPage()
	{
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadEquipment()
	{
		SetState(s =>
		{
			s.Equipment = _store.GetAllEquipment();
			s.IsLoaded = true;
		});
	}

	void NavigateToAddEquipment() => Comet.NavigationView.Navigate(this, new EquipmentDetailPage(0));

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadEquipment();

		var addToolbar = new Comet.ToolbarItem { IconGlyph = "plus", OnClicked = NavigateToAddEquipment };

		var list = new CollectionView<Equipment>(() => State.Equipment)
		{
			ViewFor = eq => MakeEquipmentCard(eq),
			ItemsLayout = Comet.ItemsLayout.Vertical(CoffeeColors.SpacingS),
			EmptyView = VStack(CoffeeColors.SpacingM,
				FormHelpers.MakeEmptyState(
					Icons.Machine,
					"No Equipment Yet",
					"Add your coffee machines, grinders, and accessories",
					iconFontFamily: Icons.CoffeeFontFamily),
				FormHelpers.MakePrimaryButton("+ Add Equipment", NavigateToAddEquipment)
			).Padding(new Thickness(CoffeeColors.SpacingL)),
		};

		return list
			.Padding(new Thickness(CoffeeColors.SpacingM))
			.Modifier(CoffeeModifiers.PageContainer)
			.ToolbarItems(addToolbar)
			.Title("Equipment");
	}

	View MakeEquipmentCard(Equipment eq)
	{
		var details = VStack(4,
			Text(eq.Name)
				.Modifier(CoffeeModifiers.CardTitle),
			Text(eq.Type.ToString())
				.Modifier(CoffeeModifiers.CardSubtitle)
		);

		var chevron = FormHelpers.MakeIcon(Icons.ChevronRight, 20, CoffeeColors.TextMuted);

		var row = HStack(CoffeeColors.SpacingS,
			details.FillHorizontal(),
			chevron
		);

		View card = Border(row)
			.Modifier(CoffeeModifiers.Card);

		card = card.OnTap(_ => Comet.NavigationView.Navigate(this, new EquipmentDetailPage(eq.Id)));

		return card;
	}
}
