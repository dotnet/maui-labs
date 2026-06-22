using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
using System.Reflection;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

public abstract class GtkMauiApplication : IPlatformApplication
{
	private const string DefaultApplicationId = "com.maui.linux";
	private const string MauiApplicationIdMetadataKey = "MauiApplicationId";

	// The backend calls gtk_css_provider_load_from_string (GTK 4.12+) and uses
	// Gtk.FileDialog (GTK 4.10+). On older GTK these resolve to missing native
	// entry points and the app crashes with a cryptic EntryPointNotFoundException.
	private const uint MinimumGtkMajor = 4;
	private const uint MinimumGtkMinor = 12;

	// Log prefix derived from the assembly name instead of a hardcoded string.
	private static readonly string LogPrefix = $"[{typeof(GtkMauiApplication).Assembly.GetName().Name}]";

	private Gtk.Application _gtkApp = null!;
	private IApplication _mauiApp = null!;
	private GtkMauiContext _applicationContext = null!;
	private string _desktopEntryName = "MAUI App";
	private readonly Dictionary<IWindow, Gtk.Window> _windows = new();

	/// <summary>
	/// Gets the current GtkMauiApplication instance.
	/// </summary>
	public static GtkMauiApplication Current =>
		(GtkMauiApplication)(IPlatformApplication.Current ?? throw new InvalidOperationException("No platform application."));

	public IServiceProvider Services { get; protected set; } = null!;
	public IApplication Application => _mauiApp;
	protected virtual string ApplicationId => ResolveApplicationId() ?? DefaultApplicationId;
	protected virtual bool CreateDesktopEntry => true;
	protected virtual string DesktopEntryName => _desktopEntryName;

	protected abstract MauiApp CreateMauiApp();

	public void Run(string[] args)
	{
		var applicationId = string.IsNullOrWhiteSpace(ApplicationId) ? null : ApplicationId;

		try
		{
			_gtkApp = Gtk.Application.New(applicationId, Gio.ApplicationFlags.DefaultFlags);
		}
		catch (Exception ex) when (IsGtkLibraryMissing(ex))
		{
			var notFound = "GTK 4 was not found. Please install GTK 4.12 or newer and try again.";
			Console.Error.WriteLine($"{LogPrefix} {notFound}");
			throw new PlatformNotSupportedException(notFound, ex);
		}

		// Application.New succeeded, so GirCore is initialized and its native calls
		// resolve — only now is it safe to query the runtime GTK version.
		EnsureSupportedGtkVersion();

		_gtkApp.OnActivate += OnActivate;
		_gtkApp.OnShutdown += OnShutdown;

		var exitCode = _gtkApp.Run(args);
		Environment.ExitCode = exitCode;
	}

	/// <summary>
	/// Verifies the GTK runtime is new enough for this backend and fails fast with
	/// an actionable message otherwise. Without this, an old GTK produces a cryptic
	/// <c>EntryPointNotFoundException</c> deep inside the first handler that loads CSS.
	/// </summary>
	/// <remarks>
	/// Must be called after <c>Gtk.Application.New</c> has succeeded — that
	/// initializes GirCore so the version functions resolve.
	/// </remarks>
	private static void EnsureSupportedGtkVersion()
	{
		var major = Gtk.Functions.GetMajorVersion();
		var minor = Gtk.Functions.GetMinorVersion();

		if (major > MinimumGtkMajor || (major == MinimumGtkMajor && minor >= MinimumGtkMinor))
			return;

		var micro = Gtk.Functions.GetMicroVersion();
		var message =
			$"This app needs GTK {MinimumGtkMajor}.{MinimumGtkMinor} or newer, but the installed " +
			$"version is {major}.{minor}.{micro}. Please update GTK and try again.";

		Console.Error.WriteLine($"{LogPrefix} {message}");
		throw new PlatformNotSupportedException(message);
	}

	// A missing libgtk surfaces either directly as DllNotFoundException or wrapped
	// in a TypeInitializationException when thrown from GirCore's type initializer.
	private static bool IsGtkLibraryMissing(Exception ex) =>
		ex is DllNotFoundException || ex.InnerException is DllNotFoundException;

	private void OnActivate(Gio.Application sender, EventArgs args)
	{
		IPlatformApplication.Current = this;

		var mauiApp = CreateMauiApp();

		var rootContext = new GtkMauiContext(mauiApp.Services);
		var applicationContext = rootContext.MakeApplicationScope(this);

		Services = applicationContext.Services;
		_applicationContext = applicationContext;
		InvokeLifecycleEvents<GtkApplicationActivated>(del => del(sender));

		// Eagerly extract and register all embedded fonts with fontconfig
		// before any widgets are created, so Pango can find them.
		(Services.GetService(typeof(IGtkFontManager)) as IGtkFontManager)?.EagerlyRegisterAllFonts();

		_mauiApp = Services.GetRequiredService<IApplication>();
		InvokeLifecycleEvents<GtkMauiApplicationCreated>(del => del(_mauiApp));

		// Wire up ApplicationHandler
		var appHandler = new ApplicationHandler();
		appHandler.SetMauiContext(applicationContext);
		appHandler.SetVirtualView(_mauiApp);

		// Monitor system theme changes
		GtkThemeManager.StartMonitoring();

		// Create the window
		CreatePlatformWindow(applicationContext);
		EnsureDesktopEntry();

		// Notify subclasses the app is fully started
		OnStarted();
	}

	private string? ResolveApplicationId()
	{
		if (TryGetApplicationId(GetType().Assembly, out var applicationId))
			return applicationId;

		var entryAssembly = Assembly.GetEntryAssembly();
		if (entryAssembly != null
			&& !ReferenceEquals(entryAssembly, GetType().Assembly)
			&& TryGetApplicationId(entryAssembly, out applicationId))
		{
			return applicationId;
		}

		return null;
	}

	private static bool TryGetApplicationId(Assembly assembly, out string applicationId)
	{
		foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
		{
			if (!string.Equals(metadata.Key, MauiApplicationIdMetadataKey, StringComparison.Ordinal))
				continue;

			if (string.IsNullOrWhiteSpace(metadata.Value))
				continue;

			applicationId = metadata.Value;
			return true;
		}

		applicationId = string.Empty;
		return false;
	}

	/// <summary>
	/// Called after the MAUI application and window have been fully initialized.
	/// Override to perform post-startup actions like starting debug agents.
	/// </summary>
	protected virtual void OnStarted() { }

	private void CreatePlatformWindow(GtkMauiContext applicationContext)
	{
		var virtualWindow = _mauiApp.CreateWindow(null);
		var gtkWindow = CreateAndShowWindow(virtualWindow);

		var windowTitle = virtualWindow.Title ?? "Microsoft.Maui.Platforms.Linux.Gtk4";
		_desktopEntryName = windowTitle;
		gtkWindow.SetDefaultSize(1024, 768);
		gtkWindow.SetSizeRequest(800, 600);
	}

	/// <summary>
	/// Creates a GTK window for a MAUI virtual window, wires up the handler,
	/// registers it in the window registry, and shows it.
	/// </summary>
	internal Gtk.Window CreateAndShowWindow(IWindow virtualWindow)
	{
		var gtkWindow = new Gtk.Window();
		gtkWindow.SetTitle(virtualWindow.Title ?? "Microsoft.Maui.Platforms.Linux.Gtk4");

		var windowContext = _applicationContext.MakeWindowScope(gtkWindow);
		windowContext.AddSpecific(gtkWindow);

		var windowHandler = new WindowHandler();
		windowHandler.SetMauiContext(windowContext);
		windowHandler.SetVirtualView(virtualWindow);

		_windows[virtualWindow] = gtkWindow;

		gtkWindow.SetApplication(_gtkApp);
		GtkDesktopIntegration.ApplyAppIcon(gtkWindow, AppContext.BaseDirectory);
		gtkWindow.Show();
		InvokeLifecycleEvents<GtkWindowCreated>(del => del(gtkWindow));

		virtualWindow.Created();
		// Activated() is fired by WindowHandler.OnNotifyIsActive when GTK reports is-active

		return gtkWindow;
	}

	/// <summary>
	/// Creates a new platform window for an OpenWindow request.
	/// Follows the MAUI pattern: calls app.CreateWindow(activationState) which
	/// returns the pending window that was passed to Application.OpenWindow().
	/// </summary>
	internal void CreateAndShowNewWindow(IApplication app, Microsoft.Maui.Handlers.OpenWindowRequest? request)
	{
		var state = request?.State;
		var activationState = state is not null
			? new ActivationState(_applicationContext, state)
			: new ActivationState(_applicationContext);
		var virtualWindow = app.CreateWindow(activationState);
		CreateAndShowWindow(virtualWindow);
	}

	/// <summary>
	/// Closes and destroys the GTK window for a MAUI virtual window.
	/// </summary>
	internal void CloseWindow(IWindow virtualWindow)
	{
		if (!_windows.TryGetValue(virtualWindow, out var gtkWindow))
			return;

		_windows.Remove(virtualWindow);
		virtualWindow.Destroying();
		gtkWindow.Close();
	}

	/// <summary>
	/// Removes a window from the registry (called when GTK closes a window directly).
	/// </summary>
	internal void UnregisterWindow(IWindow virtualWindow)
	{
		_windows.Remove(virtualWindow);
	}

	/// <summary>
	/// Gets the GTK window for a MAUI virtual window, if tracked.
	/// </summary>
	internal Gtk.Window? GetPlatformWindow(IWindow virtualWindow)
	{
		return _windows.TryGetValue(virtualWindow, out var w) ? w : null;
	}

	private void EnsureDesktopEntry()
	{
		if (!CreateDesktopEntry || string.IsNullOrWhiteSpace(ApplicationId))
			return;

		GtkDesktopIntegration.EnsureDesktopEntry(ApplicationId, DesktopEntryName, AppContext.BaseDirectory);
	}

	private void OnShutdown(Gio.Application sender, EventArgs args)
	{
		InvokeLifecycleEvents<GtkApplicationShutdown>(del => del(sender));
		_gtkApp.OnActivate -= OnActivate;
		_gtkApp.OnShutdown -= OnShutdown;
	}

	private void InvokeLifecycleEvents<TDelegate>(Action<TDelegate> action)
		where TDelegate : Delegate
	{
		if (Services == null)
			return;

		var lifecycleService = Services.GetService(typeof(ILifecycleEventService)) as ILifecycleEventService;
		lifecycleService?.InvokeEvents(typeof(TDelegate).Name, action);
	}
}
