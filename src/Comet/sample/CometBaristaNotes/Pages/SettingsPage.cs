namespace CometBaristaNotes.Pages;

public class SettingsPageState
{
	public AppThemeMode ThemeMode { get; set; }
}

public class SettingsPage : Component<SettingsPageState>
{
	readonly IThemeService _themeService;

	public SettingsPage()
	{
		_themeService = IPlatformApplication.Current!.Services.GetRequiredService<IThemeService>();
		// Note: LoadSavedTheme() is called during CoffeeTheme.Initialize() in BaristaApp ctor.
		// Calling it here during page construction would trigger ThemeManager.SetTheme() which
		// marks all views dirty and deadlocks the initial render pipeline.
	}

	public override View Render()
	{
		if (State.ThemeMode == default && _themeService.CurrentMode != default)
			SetState(s => s.ThemeMode = _themeService.CurrentMode);

		return ScrollView(
			VStack(CoffeeColors.SpacingM,
				MakeSectionLabel("Appearance")
					.Padding(new Thickness(0, CoffeeColors.SpacingM, 0, CoffeeColors.SpacingS)),
				BuildAppearanceButtons(),
				MakeSectionLabel("Manage")
					.Padding(new Thickness(0, CoffeeColors.SpacingL, 0, CoffeeColors.SpacingS)),
				BuildManageItem("Equipment", "Manage machines, grinders, and accessories", () =>
					Navigation?.Navigate(new EquipmentManagementPage())),
				BuildManageItem("Beans", "Manage coffee beans and roasters", () =>
					Navigation?.Navigate(new BeanManagementPage())),
				BuildManageItem("User Profiles", "Manage household members", () =>
					Navigation?.Navigate(new UserProfileManagementPage())),
				MakeSectionLabel("About")
					.Padding(new Thickness(0, CoffeeColors.SpacingL, 0, CoffeeColors.SpacingS)),
				BuildAboutCard()
			)
			.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title("Settings");
	}

	static View MakeSectionLabel(string title) =>
		Text(title)
			.Modifier(CoffeeModifiers.SecondaryText);

	View BuildAppearanceButtons() =>
		Grid(
			columns: new object[] { "*", "*", "*" },
			rows: new object[] { "Auto" },
			BuildThemeButton(Icons.LightMode, "Light", AppThemeMode.Light)
				.Cell(row: 0, column: 0),
			BuildThemeButton(Icons.DarkMode, "Dark", AppThemeMode.Dark)
				.Cell(row: 0, column: 1),
			BuildThemeButton(Icons.BrightnessAuto, "Auto", AppThemeMode.System)
				.Cell(row: 0, column: 2)
		).ColumnSpacing(CoffeeColors.SpacingS);

	View BuildThemeButton(string icon, string label, AppThemeMode mode)
	{
		var isSelected = State.ThemeMode == mode;
		return Border(
			VStack(4,
				Text(icon)
					.Modifier(CoffeeModifiers.IconLarge(isSelected ? CoffeeColors.Primary : CoffeeColors.TextPrimary))
					.HorizontalTextAlignment(TextAlignment.Center),
				Text(label)
					.Modifier(CoffeeModifiers.Caption)
					.Modifier(CoffeeModifiers.TextColor(isSelected ? CoffeeColors.Primary : CoffeeColors.TextPrimary))
					.HorizontalTextAlignment(TextAlignment.Center)
			)
			.Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingS))
		)
		.Modifier(CoffeeModifiers.CornerRadius(8))
		.Modifier(CoffeeModifiers.Background(isSelected ? CoffeeColors.Primary.WithAlpha(0.15f) : CoffeeColors.SurfaceVariant))
		.Modifier(CoffeeModifiers.StrokeColor(isSelected ? CoffeeColors.Primary : Colors.Transparent))
		.StrokeThickness(isSelected ? 2 : 0)
		.OnTap(_ => {
			SetState(s => s.ThemeMode = mode);
			_themeService.SetTheme(mode);
		});
	}

	View BuildManageItem(string title, string description, Action onTap) =>
		FormHelpers.MakeListCard(title, description, null, onTap);

	View BuildAboutCard() =>
		FormHelpers.MakeCard(
			VStack(CoffeeColors.SpacingXS,
				Text("BaristaNotes")
					.Modifier(CoffeeModifiers.TitleSmall),
				Text("Version 1.0")
					.Modifier(CoffeeModifiers.SecondaryText),
				Text("Track your espresso journey")
					.Modifier(CoffeeModifiers.SecondaryText)
					.Margin(new Thickness(0, CoffeeColors.SpacingXS, 0, 0))
			)
		);
}
