namespace CometTaskApp;

/// <summary>
/// Main app using TabView for multi-page navigation.
/// Exercises: TabView, CometApp, UseCometApp builder pattern.
/// </summary>
public class TaskApp : CometApp
{
	public TaskApp()
	{
		Body = CreateRootView;
	}

	public static View CreateRootView()
	{
		var tabs = TabView();
		var tasksTab = NavigationView(new TaskListPage().Title("Tasks"));
		tasksTab.TabText("Tasks");
		tasksTab.TabIcon("tab_tasks.png");
		tabs.Add(tasksTab);

		var statsTab = NavigationView(new StatsPage().Title("Stats"));
		statsTab.TabText("Stats");
		statsTab.TabIcon("tab_stats.png");
		tabs.Add(statsTab);

		var settingsTab = NavigationView(new SettingsPage().Title("Settings"));
		settingsTab.TabText("Settings");
		settingsTab.TabIcon("tab_settings.png");
		tabs.Add(settingsTab);

		return tabs;
	}
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

#if DEBUG
		builder.UseCometSampleDebugHost(TaskApp.CreateRootView);
#else
		builder.UseCometApp<TaskApp>();
#endif

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif
		return builder.Build();
	}
}
