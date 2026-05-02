namespace CometBaristaNotes.Pages;

public class BeanManagementPageState
{
	public List<Bean> Beans { get; set; } = new();
	public bool IsLoaded { get; set; }
}

public class BeanManagementPage : Component<BeanManagementPageState>
{
	readonly IDataStore _store;

	public BeanManagementPage()
	{
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadBeans()
	{
		SetState(s =>
		{
			s.Beans = _store.GetAllBeans();
			s.IsLoaded = true;
		});
	}

	void NavigateToAddBean() => Comet.NavigationView.Navigate(this, new BeanDetailPage(0));

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadBeans();

		var addToolbar = new Comet.ToolbarItem { IconGlyph = "plus", OnClicked = NavigateToAddBean };

		var list = new CollectionView<Bean>(() => State.Beans)
		{
			ViewFor = bean => MakeBeanCard(bean),
			ItemsLayout = Comet.ItemsLayout.Vertical(CoffeeColors.SpacingS),
			EmptyView = VStack(CoffeeColors.SpacingM,
				FormHelpers.MakeEmptyState(
					Icons.Coffee,
					"No Beans Yet",
					"Add your first bean to start tracking your coffee collection"),
				FormHelpers.MakePrimaryButton("+ Add Bean", NavigateToAddBean)
			).Padding(new Thickness(CoffeeColors.SpacingL)),
		};

		return list
			.Padding(new Thickness(CoffeeColors.SpacingM))
			.Modifier(CoffeeModifiers.PageContainer)
			.ToolbarItems(addToolbar)
			.Title("Beans");
	}

	View MakeBeanCard(Bean bean)
	{
		var details = VStack(4,
			Text(bean.Name)
				.Modifier(CoffeeModifiers.CardTitle)
		);

		if (bean.Roaster != null)
		{
			details.Add(HStack(6,
				FormHelpers.MakeIcon(Icons.Factory, CoffeeColors.IconSizeSmall, CoffeeColors.TextMuted),
				Text(bean.Roaster)
					.Modifier(CoffeeModifiers.CardSubtitle)
			));
		}

		View card = Border(details.FillHorizontal())
			.Modifier(CoffeeModifiers.Card);

		card = card.OnTap(_ => Comet.NavigationView.Navigate(this, new BeanDetailPage(bean.Id)));

		return card;
	}
}
