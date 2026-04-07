using Comet;
using Comet.Styles;
using CometBaristaNotes.Components;
using CometBaristaNotes.Pages;
using CometBaristaNotes.Services;
using CometBaristaNotes.Styles;
using Microsoft.Maui.Graphics;
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

		// New Shot tab — native toolbar items (camera first = rightmost on iOS, mic second = leftmost)
		var shotNav = MakeTab(new ShotLoggingPage(), "New Shot", "cup.and.saucer.fill");
		shotNav.ToolbarItems.Add(new Comet.ToolbarItem { IconGlyph = "camera.fill", OnClicked = () => { /* open camera */ } });
		shotNav.ToolbarItems.Add(new Comet.ToolbarItem { IconGlyph = "mic.fill", OnClicked = () => { /* toggle voice */ } });
		tabs.Add(shotNav);

		var activityPage = new ActivityFeedPage();
		var activityNav = MakeTab(activityPage, "Activity", "list.bullet.rectangle.portrait.fill");
		activityNav.ToolbarItems.Add(new Comet.ToolbarItem { IconGlyph = "line.3.horizontal.decrease", OnClicked = () => activityPage.TriggerFilter() });
		tabs.Add(activityNav);
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
		nav.SetAutomationId($"barista-{title.Replace(" ", string.Empty).ToLowerInvariant()}-tab-root");
		nav.TabText(title);
		nav.TabIcon(icon);
		return nav.Background(CoffeeColors.Background).IgnoreSafeArea();
	}
}
