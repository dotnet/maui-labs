using CometProjectManager.Pages;
using CommunityToolkit.Maui;
using Microsoft.Maui.ApplicationModel;
using Syncfusion.Maui.Toolkit.Hosting;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using MauiPage = Microsoft.Maui.Controls.ContentPage;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace CometProjectManager;

/// <summary>
/// CometApp used only for snapshot/force-page testing mode.
/// Normal launch uses ShellMauiApp with real MAUI Shell navigation.
/// </summary>
public class ProjectManagerApp : CometApp
{
	static string? _forcePage = null;
	public static void SetForcePage(string page) => _forcePage = page;
	public static string? ForcePage => _forcePage;

	[Body]
	Comet.View body()
	{
		var store = DataStore.Instance;
		var firstProject = store.Projects.Value?.FirstOrDefault();
		var firstTask = store.AllTasks.Value?.FirstOrDefault();
		return (_forcePage ?? "dashboard") switch
		{
			"dashboard" => new DashboardPage(),
			"projects" => new ProjectListPage(),
			"manage" => new ManageMetaPage(),
			"projectdetail" => new ProjectDetailPage(firstProject ?? new CometProjectManager.Models.Project()),
			"taskdetail" => new TaskDetailPage(firstTask, firstTask?.ProjectID ?? 1),
			_ => new DashboardPage(),
		};
	}
}

/// <summary>
/// MAUI Shell providing real flyout/hamburger navigation identical to the XAML reference app.
/// Each page wraps Comet MVU views via MauiViewHost.
/// </summary>
public class ProjectManagerShell : MauiShell
{
	public ProjectManagerShell()
	{
		FlyoutBehavior = FlyoutBehavior.Flyout;
		
		// Match MAUI reference Shell styling
		MauiShell.SetBackgroundColor(this, Color.FromArgb("#F2F2F2"));
		MauiShell.SetForegroundColor(this, Colors.Black);
		MauiShell.SetTitleColor(this, Colors.Black);
		MauiShell.SetNavBarHasShadow(this, false);

		Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Dashboard",
			Icon = MakeIcon(Fonts.FluentUI.diagram_24_regular),
			ContentTemplate = new DataTemplate(() => MakeCometPage(new DashboardPage(wrapInNav: false), DataStore.Instance.Today)),
			Route = "dashboard"
		});

		Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Projects",
			Icon = MakeIcon(Fonts.FluentUI.list_24_regular),
			ContentTemplate = new DataTemplate(() => MakeCometPage(new ProjectListPage(wrapInNav: false), "Projects")),
			Route = "projects"
		});

		Items.Add(new Microsoft.Maui.Controls.ShellContent
		{
			Title = "Manage Meta",
			Icon = MakeIcon(Fonts.FluentUI.info_24_regular),
			ContentTemplate = new DataTemplate(() => MakeCometPage(new ManageMetaPage(wrapInNav: false), "Manage Meta")),
			Route = "manage"
		});

		// Register detail routes
		Routing.RegisterRoute("project", typeof(ProjectDetailShellPage));
		Routing.RegisterRoute("task", typeof(TaskDetailShellPage));

		// Flyout footer with theme switcher (matches MAUI reference AppShell)
		var themeControl = new Syncfusion.Maui.Toolkit.SegmentedControl.SfSegmentedControl
		{
			VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
			HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
			SegmentWidth = 40,
			SegmentHeight = 40,
		};
		themeControl.ItemsSource = new Syncfusion.Maui.Toolkit.SegmentedControl.SfSegmentItem[]
		{
			new() { ImageSource = MakeIcon(Fonts.FluentUI.weather_sunny_28_regular) },
			new() { ImageSource = MakeIcon(Fonts.FluentUI.weather_moon_28_regular) },
		};
		var currentTheme = Application.Current?.RequestedTheme ?? AppTheme.Light;
		themeControl.SelectedIndex = currentTheme == AppTheme.Light ? 0 : 1;
		themeControl.SelectionChanged += (s, e) =>
		{
			if (Application.Current != null)
				Application.Current.UserAppTheme = e.NewIndex == 0 ? AppTheme.Light : AppTheme.Dark;
		};
		FlyoutFooter = new Microsoft.Maui.Controls.Grid
		{
			Padding = new Thickness(15),
			Children = { themeControl }
		};
	}

	static Microsoft.Maui.Controls.FontImageSource MakeIcon(string glyph) => new Microsoft.Maui.Controls.FontImageSource
	{
		Glyph = glyph,
		FontFamily = Fonts.FluentUI.FontFamily,
		Color = Color.FromArgb("#0D0D0D"),
		Size = 24
	};

	/// <summary>
	/// Wraps a Comet View in a MAUI ContentPage for Shell hosting.
	/// Uses Loaded event to embed the Comet view's platform representation.
	/// </summary>
	static MauiPage MakeCometPage(Comet.View cometView, string title)
	{
		var page = new MauiPage
		{
			Title = title,
			BackgroundColor = Color.FromArgb("#F2F2F2"),
		};
		
		// Create a ContentView container
		var container = new Microsoft.Maui.Controls.ContentView
		{
			BackgroundColor = Color.FromArgb("#F2F2F2"),
		};
		
		page.Content = container;
		
		// When the page is loaded and has a handler/MauiContext, embed the Comet view
		page.Loaded += (s, e) =>
		{
			if (page.Handler?.MauiContext == null) return;
			EmbedCometView(container, cometView, page.Handler.MauiContext);
		};
		
		MauiShell.SetNavBarIsVisible(page, true);
		return page;
	}
	
	internal static void EmbedCometView(Microsoft.Maui.Controls.ContentView container, Comet.View cometView, IMauiContext mauiContext)
	{
		try
		{
			// Get the render view (body content). For our pages this returns MauiViewHost.
			var renderView = cometView.GetView();
			IView viewToRender = (renderView != null && renderView != cometView) ? renderView : cometView;
			
			// If it's a MauiViewHost, extract the hosted MAUI view directly
			if (viewToRender is MauiViewHost mvh)
			{
				var hostedView = mvh.HostedView;
				if (hostedView is Microsoft.Maui.Controls.View mauiView)
				{
					container.Content = mauiView;
					return;
				}
			}
			
			// Fallback: use CometHost wrapper
			container.Content = new CometHost(cometView);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[EmbedCometView] Failed: {ex.Message}");
		}
	}
}

/// <summary>
/// Project detail page for Shell navigation
/// </summary>
[QueryProperty(nameof(ProjectId), "id")]
public class ProjectDetailShellPage : MauiPage
{
	string _projectId = "";
	Microsoft.Maui.Controls.ContentView _container = new();
	Comet.View? _cometView;
	bool _embedded;

	public ProjectDetailShellPage()
	{
		Content = _container;
	}

	public string ProjectId
	{
		get => _projectId;
		set
		{
			_projectId = value;
			LoadProject();
		}
	}

	void LoadProject()
	{
		if (!int.TryParse(_projectId, out var id)) return;
		var project = DataStore.Instance.Projects.Value?.FirstOrDefault(p => p.ID == id)
			?? new CometProjectManager.Models.Project();
		Title = "Project";
		_cometView?.Dispose();
		_cometView = new ProjectDetailPage(project, wrapInNav: false);
		_embedded = false;
		TryEmbed();
	}

	void TryEmbed()
	{
		if (_embedded || _cometView == null || Handler?.MauiContext == null) return;
		ProjectManagerShell.EmbedCometView(_container, _cometView, Handler.MauiContext);
		_embedded = true;
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		TryEmbed();
	}
}

/// <summary>
/// Task detail page for Shell navigation
/// </summary>
[QueryProperty(nameof(TaskId), "id")]
public class TaskDetailShellPage : MauiPage
{
	string _taskId = "";
	Microsoft.Maui.Controls.ContentView _container = new();
	Comet.View? _cometView;
	bool _embedded;

	public TaskDetailShellPage()
	{
		Content = _container;
	}

	public string TaskId
	{
		get => _taskId;
		set
		{
			_taskId = value;
			LoadTask();
		}
	}

	void LoadTask()
	{
		if (!int.TryParse(_taskId, out var id)) return;
		var task = DataStore.Instance.AllTasks.Value?.FirstOrDefault(t => t.ID == id);
		var projectId = task?.ProjectID ?? 1;
		Title = "Task";
		_cometView?.Dispose();
		_cometView = new TaskDetailPage(task, projectId, wrapInNav: false);
		_embedded = false;
		TryEmbed();
	}

	void TryEmbed()
	{
		if (_embedded || _cometView == null || Handler?.MauiContext == null) return;
		ProjectManagerShell.EmbedCometView(_container, _cometView, Handler.MauiContext);
		_embedded = true;
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		TryEmbed();
	}
}

/// <summary>
/// Standard MAUI Application with Shell for proper flyout navigation.
/// </summary>
public class ShellMauiApp : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new ProjectManagerShell());
	}
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var args = System.Environment.GetCommandLineArgs();
		foreach (var arg in args)
		{
			if (arg.StartsWith("--page=", System.StringComparison.OrdinalIgnoreCase))
			{
				ProjectManagerApp.SetForcePage(arg.Substring(7).ToLowerInvariant());
			}
		}

		var builder = MauiApp.CreateBuilder();

		if (ProjectManagerApp.ForcePage != null)
		{
			// Snapshot testing mode: use CometApp
			builder.UseCometApp<ProjectManagerApp>();
		}
		else
		{
			// Normal mode: use real MAUI Shell
			builder
				.UseMauiApp<ShellMauiApp>()
				.UseMauiCommunityToolkit();
		}

		builder.ConfigureSyncfusionToolkit()
			.UseCometHandlers()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
				fonts.AddFont("FluentSystemIcons-Regular.ttf", Fonts.FluentUI.FontFamily);
			});

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}
}
