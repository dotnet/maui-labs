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
		HStack(CoffeeColors.SpacingS,
			BuildThemeButton(Icons.LightMode, "Light", AppThemeMode.Light),
			BuildThemeButton(Icons.DarkMode, "Dark", AppThemeMode.Dark),
			BuildThemeButton(Icons.BrightnessAuto, "Auto", AppThemeMode.System),
			new Spacer()
		);

	View BuildThemeButton(string icon, string label, AppThemeMode mode)
	{
		var isSelected = State.ThemeMode == mode;
		var accent = isSelected ? CoffeeColors.Primary : CoffeeColors.TextPrimary;
		return Border(
			VStack(CoffeeColors.SpacingS,
				Text(icon)
					.Modifier(CoffeeModifiers.IconXLarge(accent))
					.HorizontalTextAlignment(TextAlignment.Center),
				Text(label)
					.Modifier(CoffeeModifiers.Body)
					.Modifier(CoffeeModifiers.TextColor(accent))
					.HorizontalTextAlignment(TextAlignment.Center)
			)
			.Padding(new Thickness(CoffeeColors.SpacingM, CoffeeColors.SpacingL))
		)
		.Modifier(CoffeeModifiers.FrameSize(90, 90))
		.Modifier(CoffeeModifiers.CornerRadius(12))
		.Modifier(CoffeeModifiers.Background(isSelected ? CoffeeColors.Primary.WithAlpha(0.15f) : CoffeeColors.SurfaceElevated))
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
