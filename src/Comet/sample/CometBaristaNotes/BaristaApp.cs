using CometBaristaNotes.Pages;
using TabView = Comet.TabView;

namespace CometBaristaNotes;

public class BaristaApp : CometApp
{
	public BaristaApp()
	{
		// Initialize the coffee theme system via DI-registered ThemeService
		var themeService = ServiceHelper.Services?.GetService<IThemeService>();
		if (themeService != null)
			CoffeeTheme.Initialize(themeService);
		else
			ThemeManager.SetTheme(CoffeeTheme.Light);

		Body = CreateRootView;
	}

	public static Comet.View CreateRootView()
	{
		var tabs = TabView();
		tabs.Add(MakeTab(new ShotLoggingPage(), "New Shot", "cup.and.saucer.fill"));
		tabs.Add(MakeTab(new ActivityFeedPage(), "Activity", "list.bullet.rectangle.portrait.fill"));
		tabs.Add(MakeTab(new SettingsPage(), "Settings", "gearshape.fill"));
		tabs.TabBarBackgroundColor(CoffeeColors.Background);
		tabs.TabBarTintColor(CoffeeColors.Primary);
		tabs.TabBarUnselectedColor(CoffeeColors.TextSecondary);
		return tabs;
	}

	static NavigationView MakeTab(Comet.View page, string title, string icon)
	{
		var nav = NavigationView(page);
		nav.SetEnvironment("NavigationBackgroundColor", CoffeeColors.Background);
		nav.SetEnvironment("NavigationTextColor", CoffeeColors.TextPrimary);
		nav.NavigationPrefersLargeTitles(true);
		nav.SetAutomationId($"barista-{title.Replace(" ", string.Empty).ToLowerInvariant()}-tab-root");
		nav.TabText(title);
		nav.TabIcon(icon);
		return nav.Background(CoffeeColors.Background).IgnoreSafeArea();
	}
}
