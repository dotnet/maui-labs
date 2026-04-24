namespace CometBaristaNotes.Pages;

public class UserProfileManagementPageState
{
	public List<UserProfile> Profiles { get; set; } = new();
	public bool IsLoaded { get; set; }
}

public class UserProfileManagementPage : Component<UserProfileManagementPageState>
{
	readonly IDataStore _store;

	public UserProfileManagementPage()
	{
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadProfiles()
	{
		SetState(s =>
		{
			s.Profiles = _store.GetAllProfiles();
			s.IsLoaded = true;
		});
	}

	void NavigateToAddProfile() => Comet.NavigationView.Navigate(this, new ProfileFormPage(0));

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadProfiles();

		var addToolbar = new Comet.ToolbarItem { IconGlyph = "plus", OnClicked = NavigateToAddProfile };

		var list = new CollectionView<UserProfile>(() => State.Profiles)
		{
			ViewFor = profile => MakeProfileCard(profile),
			ItemsLayout = Comet.ItemsLayout.Vertical(CoffeeColors.SpacingS),
			EmptyView = VStack(CoffeeColors.SpacingM,
				FormHelpers.MakeEmptyState(
					Icons.Person,
					"No Profiles Yet",
					"Create profiles for different users or coffee preferences"),
				FormHelpers.MakePrimaryButton("+ Add Profile", NavigateToAddProfile)
			).Padding(new Thickness(CoffeeColors.SpacingL)),
		};

		return list
			.Padding(new Thickness(CoffeeColors.SpacingM))
			.Modifier(CoffeeModifiers.PageContainer)
			.ToolbarItems(addToolbar)
			.Title("Profiles");
	}

	View MakeProfileCard(UserProfile profile)
	{
		var details = VStack(4,
			Text(profile.Name)
				.Modifier(CoffeeModifiers.CardTitle),
			Text($"Created: {profile.CreatedAt:MMM d, yyyy}")
				.Modifier(CoffeeModifiers.CardSubtitle)
		);

		var chevron = FormHelpers.MakeIcon(Icons.ChevronRight, 20, CoffeeColors.TextMuted);

		var row = HStack(CoffeeColors.SpacingS,
			details.FillHorizontal(),
			chevron
		);

		View card = Border(row)
			.Modifier(CoffeeModifiers.Card);

		card = card.OnTap(_ => Comet.NavigationView.Navigate(this, new ProfileFormPage(profile.Id)));

		return card;
	}
}
