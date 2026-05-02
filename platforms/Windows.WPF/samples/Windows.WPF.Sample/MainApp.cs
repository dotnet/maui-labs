using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Windows.WPF.Sample.Pages;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample;

// MAUI Application subclass (platform-agnostic).
class MainApp : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainShell()) { Title = "WPF Control Gallery" };
	}
}

class MainShell : FlyoutPage
{
	private readonly (string name, Func<Page> factory)[] _pages =
	[
		("🏠 Home", () => new HomePage()),
		("🧩 XAML Runtime", () => new XamlRuntimePage()),
		("🖼️ Image / Font / Icon", () => new ResourceAssetsPage()),
		("🖼️ Image Showcase", () => new ImagePage()),
		("🎛️ Controls", () => new ControlsPage()),
		("📅 Pickers & Search", () => new PickersPage()),
		("📐 Layouts", () => new LayoutsPage()),
		("📐 Flex & Absolute", () => new LayoutsAdvancedPage()),
		("🔤 FormattedText", () => new FormattedTextPage()),
		("🔷 Shapes", () => new ShapesPage()),
		("🔄 Transforms & Effects", () => new TransformsPage()),
		("💬 Alerts & Dialogs", () => new AlertsPage()),
		("📋 Collection View", () => new CollectionViewPage()),
		("📜 ListView & TableView", () => new ListViewTableViewPage()),
		("✏️ Text Input Styling", () => new TextInputStylingPage()),
		("🍔 Menu & Toolbar", () => new MenuBarPage()),
		("🔄 Refresh & Swipe", () => new RefreshSwipePage()),
		("🎠 Carousel & Indicators", () => new CarouselIndicatorPage()),
		("🎨 Graphics", () => new GraphicsPage()),
		("🧪 Graphics Features", () => new GraphicsFeaturePage()),
		("📱 Device & App Info", () => new DeviceInfoPage()),
		("🔋 Battery & Network", () => new BatteryNetworkPage()),
		("📋 Clipboard & Storage", () => new ClipboardPrefsPage()),
		("🚀 Launch & Share", () => new LaunchSharePage()),
		("🌐 Blazor Hybrid", () => new BlazorPage()),
		("🧭 Navigation", () => new NavigationPage(new NavigationDemoPage())),
		("📑 TabbedPage", () => new TabbedPageDemo()),
		("📂 FlyoutPage", () => new FlyoutPageDemo()),
		("🐚 Shell Navigation", () => new ShellDemoPage()),
		("🧬 ControlTemplate", () => new ControlTemplatePage()),
		("🪟 Modal Pages", () => new ModalDemoPage()),
		("🪟 Multi-Window", () => new MultiWindowPage()),
		("🎨 Theme", () => new ThemePage()),
	];

	public MainShell()
	{
		Title = "Microsoft.Maui.Platforms.Windows.WPF Demo";

		var menuItems = _pages
			.Select(p => new MenuEntry(p.name, "menu-" + Slug(p.name), p.factory))
			.ToList();

		var menuList = new CollectionView
		{
			ItemsSource = menuItems,
			SelectionMode = SelectionMode.Single,
			SelectedItem = menuItems[0],
			VerticalOptions = LayoutOptions.Fill,
			Header = "WPF Demo",
			AutomationId = "MainMenu",
			ItemTemplate = new DataTemplate(() =>
			{
				var lbl = new Label
				{
					Padding = new Thickness(12, 8),
					FontSize = 14,
				};
				lbl.SetBinding(Label.TextProperty, nameof(MenuEntry.Name));
				lbl.SetBinding(SemanticProperties.DescriptionProperty, nameof(MenuEntry.Name));
				lbl.SetBinding(Label.AutomationIdProperty, nameof(MenuEntry.AutomationId));
				return lbl;
			}),
		};

		menuList.SelectionChanged += (s, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is MenuEntry selected && selected.Factory != null)
			{
				var page = selected.Factory();
				if (page is ContentPage cp)
					Detail = new NavigationPage(cp);
				else
					Detail = page;
			}
		};

		Flyout = new ContentPage
		{
			Title = "Menu",
			Content = menuList,
		};

		Detail = new NavigationPage(new HomePage());
		IsPresented = true;

		_instance = this;
		StartAuditServer();
	}

	static MainShell? _instance;
	static System.Net.HttpListener? _listener;

	void StartAuditServer()
	{
		if (_listener != null) return;
		try
		{
			_listener = new System.Net.HttpListener();
			_listener.Prefixes.Add("http://localhost:9224/");
			_listener.Start();
			_ = System.Threading.Tasks.Task.Run(AuditLoop);

			// Stop the listener cleanly on app shutdown so the HTTP port is released
			// and the AuditLoop task can exit.
			var app = System.Windows.Application.Current;
			if (app != null)
			{
				app.Exit += (_, _) => StopAuditServer();
			}
		}
		catch { _listener = null; }
	}

	static void StopAuditServer()
	{
		var listener = _listener;
		_listener = null;
		if (listener == null) return;
		try { listener.Stop(); } catch { }
		try { listener.Close(); } catch { }
	}

	async System.Threading.Tasks.Task AuditLoop()
	{
		while (_listener != null && _listener.IsListening)
		{
			System.Net.HttpListenerContext ctx;
			try { ctx = await _listener.GetContextAsync(); }
			catch (System.Net.HttpListenerException) { break; }
			catch (ObjectDisposedException) { break; }
			catch (InvalidOperationException) { break; }

			string resp = "ok";
			try
			{
				var q = ctx.Request.QueryString;
				var path = ctx.Request.Url?.AbsolutePath ?? "";
				if (path == "/pages")
				{
					resp = string.Join("\n", _pages.Select((p, i) => $"{i}\t{p.name}"));
				}
				else if (path == "/goto" && int.TryParse(q["index"], out var idx) && idx >= 0 && idx < _pages.Length)
				{
					string? err = null;
					await Dispatcher.DispatchAsync(() =>
					{
						try
						{
							var page = _pages[idx].factory();
							if (page is ContentPage cp) Detail = new NavigationPage(cp);
							else Detail = page;
							IsPresented = false;
						}
						catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
					});
					resp = err != null ? "ERR: " + err : Slug(_pages[idx].name);
				}
			}
			catch (Exception ex) { resp = "ERR: " + ex.Message; }
			try
			{
				var bytes = System.Text.Encoding.UTF8.GetBytes(resp);
				ctx.Response.ContentLength64 = bytes.Length;
				ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
				ctx.Response.Close();
			}
			catch { }
		}
	}

	static string Slug(string name)
	{
		var chars = name.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray();
		return new string(chars).Trim().Replace(' ', '-').ToLowerInvariant();
	}

	sealed record MenuEntry(string Name, string AutomationId, Func<Page> Factory);
}
