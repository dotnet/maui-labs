using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Comet;
using Comet.Styles;
using CometControlsGallery.Pages;
using MauiIcons.Cupertino;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Devices;
using Microsoft.Maui.LifecycleEvents;
using static Comet.CometControls;
#if DEBUG
using MauiDevFlow.Agent;
#endif
#if __MACOS__
using Microsoft.Maui.Platform.MacOS.Hosting;
#endif
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiContentPage = Microsoft.Maui.Controls.ContentPage;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace CometControlsGallery
{
	public class NavItem
	{
		public string Title { get; set; } = "";
		public string Icon { get; set; } = "";
		public Func<View> CreatePage { get; set; } = () => new Text("Empty");
		public string Category { get; set; } = "";
	}

	public class App : MauiApplication
	{
		static System.Net.Sockets.TcpListener? _navServer;
		const int NavPort = 10254;

		protected override MauiWindow CreateWindow(IActivationState activationState)
		{
			var page = new MauiContentPage
			{
				Padding = 0,
				Content = new CometHost(new SidebarLayout())
			};
			StartNavigationServer();
			return new MauiWindow(page);
		}

		static void StartNavigationServer()
		{
			if (_navServer != null) return;
			try
			{
				_navServer = new System.Net.Sockets.TcpListener(IPAddress.Loopback, NavPort);
				_navServer.Start();
				_ = Task.Run(async () =>
				{
					while (true)
					{
						try
						{
							var client = await _navServer.AcceptTcpClientAsync();
							_ = Task.Run(() => HandleNavClient(client));
						}
						catch { break; }
					}
				});
				System.Diagnostics.Debug.WriteLine($"[Gallery] Navigation server on port {NavPort}");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Gallery] NavServer failed: {ex.Message}");
			}
		}

		static void HandleNavClient(System.Net.Sockets.TcpClient client)
		{
			try
			{
				using var stream = client.GetStream();
				using var reader = new System.IO.StreamReader(stream);
				var requestLine = reader.ReadLine() ?? "";
				// Read headers until blank line
				while (reader.ReadLine() is string line && line.Length > 0) { }

				// Parse: "GET /path?query HTTP/1.1"
				var parts = requestLine.Split(' ');
				var url = parts.Length > 1 ? parts[1] : "/";
				var uri = new Uri("http://localhost" + url);
				var path = uri.AbsolutePath;
				var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

				string json;
				if (path == "/pages")
				{
					var titles = SidebarLayout.Current?.GetPageTitles() ?? (IReadOnlyList<string>)Array.Empty<string>();
					json = JsonSerializer.Serialize(new { pages = titles });
				}
				else if (path == "/navigate")
				{
					var page = query["page"];
					var indexStr = query["index"];

					if (!string.IsNullOrEmpty(page))
					{
						var ok = SidebarLayout.Current?.NavigateToPage(page) ?? false;
						json = JsonSerializer.Serialize(new { success = ok, page });
					}
					else if (int.TryParse(indexStr, out var idx))
					{
						SidebarLayout.Current?.NavigateToIndex(idx);
						json = JsonSerializer.Serialize(new { success = true, index = idx });
					}
					else
					{
						json = JsonSerializer.Serialize(new { success = false, error = "Provide ?page=Name or ?index=N" });
					}
				}
				else
				{
					json = JsonSerializer.Serialize(new
					{
						endpoints = new[] { "GET /pages", "GET /navigate?page=Home", "GET /navigate?index=0" }
					});
				}

				var body = System.Text.Encoding.UTF8.GetBytes(json);
				var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
				var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
				stream.Write(headerBytes, 0, headerBytes.Length);
				stream.Write(body, 0, body.Length);
			}
			catch { }
			finally { client.Close(); }
		}

		public static MauiApp CreateMauiApp()
		{
			// Global unhandled exception logging
			AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			{
				var ex = e.ExceptionObject as Exception;
				System.Diagnostics.Debug.WriteLine($"[CRASH] Unhandled: {ex}");
				Console.Error.WriteLine($"[CRASH] Unhandled: {ex}");
			};

			var builder = MauiApp.CreateBuilder();

#if __MACOS__
			builder.UseMauiAppMacOS<App>();
#else
			builder.UseMauiApp<App>();
#endif
			builder.UseCometHandlers();
#if !__MACOS__
			builder.UseCupertinoMauiIcons();
#endif

			// Override default button style to match MAUI's native Mac Catalyst look
			var theme = ThemeManager.Current()
				.SetControlStyle<Button, ButtonConfiguration>(ButtonStyles.Text);
			ThemeManager.SetTheme(theme);

#if DEBUG && !__MACOS__
			builder.AddMauiDevFlowAgent();
#endif

#if MACCATALYST
			builder.ConfigureLifecycleEvents(events =>
			{
				events.AddiOS(ios =>
				{
					ios.SceneWillConnect((scene, session, options) =>
					{
						if (scene is UIKit.UIWindowScene windowScene)
						{
							windowScene.SizeRestrictions.MinimumSize = new CoreGraphics.CGSize(900, 600);
							windowScene.SizeRestrictions.MaximumSize = new CoreGraphics.CGSize(2000, 1400);
						}
					});
				});
			});
#endif

			builder.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

			return builder.Build();
		}
	}

	public class SidebarLayout : View
	{
		readonly Reactive<int> selectedIndex = 0;
		NavigationView? _mainNav;
		NavigationView? _phoneNav;

		/// <summary>
		/// Singleton for programmatic navigation (e.g., from MauiDevFlow automation).
		/// </summary>
		public static SidebarLayout? Current { get; private set; }

		public SidebarLayout()
		{
			Current = this;
		}

		/// <summary>
		/// Navigate to a page by index. Thread-safe — dispatches to main thread.
		/// </summary>
		public void NavigateToIndex(int index)
		{
			if (index < 0 || index >= navItems.Count) return;

			if (Microsoft.Maui.Controls.Application.Current?.Dispatcher is { } dispatcher)
			{
				dispatcher.Dispatch(() =>
				{
#if !__MACOS__
					if (DeviceInfo.Idiom == DeviceIdiom.Phone && _phoneNav != null)
					{
						// On phone, push navigation directly — no signal write needed
						// since the page list doesn't depend on selectedIndex.
						_phoneNav.PopToRoot();
						var detail = navItems[index].CreatePage()
							.Title(navItems[index].Title);
						_phoneNav.Navigate(detail);
					}
					else
#endif
					{
						// Desktop sidebar: signal write triggers body rebuild to swap detail
						selectedIndex.Value = index;
					}
				});
			}
			else
			{
				selectedIndex.Value = index;
			}
		}

		/// <summary>
		/// Navigate to a page by title (case-insensitive). Returns true if found.
		/// </summary>
		public bool NavigateToPage(string title)
		{
			var idx = navItems.FindIndex(n =>
				n.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
			if (idx < 0) return false;
			NavigateToIndex(idx);
			return true;
		}

		/// <summary>Returns page titles for automation discovery.</summary>
		public IReadOnlyList<string> GetPageTitles() =>
			navItems.Select(n => n.Title).ToList();

		static readonly Color SidebarBackground = Color.FromArgb("#F2F2F7");
		static readonly Color CategoryHeaderColor = Colors.Grey;
		static readonly Color ItemTextColor = new Color(60, 60, 67);
		static readonly Color SelectedAccent = Color.FromArgb("#007AFF");

		static readonly List<NavItem> navItems = new()
		{
			// General
			new NavItem { Title = "Home", Category = "General", Icon = "house.fill", CreatePage = () => new HomePage() },
			new NavItem { Title = "Controls", Category = "General", Icon = "slider.horizontal.3", CreatePage = () => new ControlsPage() },
			new NavItem { Title = "RadioButton", Category = "General", Icon = "circle.inset.filled", CreatePage = () => new RadioButtonPage() },
			new NavItem { Title = "Pickers & Search", Category = "General", Icon = "magnifyingglass", CreatePage = () => new PickersPage() },
			new NavItem { Title = "Fonts", Category = "General", Icon = "textformat", CreatePage = () => new FontsPage() },
			new NavItem { Title = "Formatted Text", Category = "General", Icon = "text.badge.star", CreatePage = () => new FormattedTextPage() },
			new NavItem { Title = "Layouts", Category = "General", Icon = "rectangle.3.group", CreatePage = () => new LayoutsPage() },
			new NavItem { Title = "Alerts & Dialogs", Category = "General", Icon = "exclamationmark.triangle", CreatePage = () => new AlertsPage() },
			// Lists & Collections
			new NavItem { Title = "Collection View", Category = "Lists & Collections", Icon = "square.grid.2x2", CreatePage = () => new CollectionViewPage() },
			new NavItem { Title = "CarouselView", Category = "Lists & Collections", Icon = "rectangle.stack", CreatePage = () => new CarouselViewPage() },
			new NavItem { Title = "TableView", Category = "Lists & Collections", Icon = "tablecells", CreatePage = () => new TableViewPage() },
			// Drawing & Visual
			new NavItem { Title = "Graphics", Category = "Drawing & Visual", Icon = "paintbrush", CreatePage = () => new GraphicsPage() },
			new NavItem { Title = "Gestures", Category = "Drawing & Visual", Icon = "hand.tap", CreatePage = () => new GesturesPage() },
			new NavItem { Title = "Shapes", Category = "Drawing & Visual", Icon = "star", CreatePage = () => new ShapesPage() },
			new NavItem { Title = "Transforms", Category = "Drawing & Visual", Icon = "arrow.triangle.2.circlepath", CreatePage = () => new TransformsPage() },
			// Platform
			new NavItem { Title = "Menu Bar", Category = "Platform", Icon = "menubar.rectangle", CreatePage = () => new MenuBarPage() },
			new NavItem { Title = "Theme", Category = "Platform", Icon = "sun.max", CreatePage = () => new ThemePage() },
			new NavItem { Title = "Style System", Category = "Platform", Icon = "paintpalette", CreatePage = () => new StyleSystemPage() },
			new NavItem { Title = "WebView", Category = "Platform", Icon = "globe", CreatePage = () => new WebViewPage() },
			new NavItem { Title = "Device & App Info", Category = "Platform", Icon = "iphone", CreatePage = () => new DeviceInfoPage() },
			new NavItem { Title = "Battery & Network", Category = "Platform", Icon = "battery.100", CreatePage = () => new BatteryNetworkPage() },
			new NavItem { Title = "Clipboard & Storage", Category = "Platform", Icon = "doc.on.clipboard", CreatePage = () => new ClipboardStoragePage() },
			new NavItem { Title = "Launch & Share", Category = "Platform", Icon = "square.and.arrow.up", CreatePage = () => new LaunchSharePage() },
			// Navigation
			new NavItem { Title = "TabbedPage", Category = "Navigation", Icon = "rectangle.split.3x1", CreatePage = () => new TabbedPageDemoPage() },
			new NavItem { Title = "FlyoutPage", Category = "Navigation", Icon = "sidebar.left", CreatePage = () => new FlyoutPageDemoPage() },
			// State Management
			new NavItem { Title = "Signal Counter", Category = "State Management", Icon = "number", CreatePage = () => new SignalCounterPage() },
			new NavItem { Title = "Computed Demo", Category = "State Management", Icon = "function", CreatePage = () => new ComputedDemoPage() },
			new NavItem { Title = "Two-Way Binding", Category = "State Management", Icon = "arrow.left.arrow.right", CreatePage = () => new TwoWayBindingPage() },
			new NavItem { Title = "Signal List", Category = "State Management", Icon = "list.bullet.rectangle", CreatePage = () => new SignalListPage() },
			new NavItem { Title = "Coalescing Demo", Category = "State Management", Icon = "bolt", CreatePage = () => new CoalescingDemoPage() },
			new NavItem { Title = "State Preservation", Category = "State Management", Icon = "arrow.uturn.backward", CreatePage = () => new StatePreservationPage() },
		};

		[Body]
		View body()
		{
#if __MACOS__
			// macOS is always desktop — DeviceInfo.Idiom uses MAUI Essentials which
			// has no macOS implementation, so skip the runtime check.
			return BuildDesktopLayout();
#else
			if (DeviceInfo.Idiom == DeviceIdiom.Phone)
				return BuildPhoneLayout();

			return BuildDesktopLayout();
#endif
		}

		View BuildDesktopLayout()
		{
			var sidebar = BuildSidebar();
			var idx = selectedIndex.Value;
			var detail = navItems[idx].CreatePage();

			_mainNav = NavigationView(detail)
				.Title(navItems[idx].Title);

			var separator = new Spacer()
				.Background(Colors.Grey)
				.Opacity(0.3f);

			return Grid(
				new object[] { 280, 1, "*" },
				null,
				sidebar.Cell(row: 0, column: 0),
				separator.Cell(row: 0, column: 1),
				_mainNav.Cell(row: 0, column: 2)
			);
		}

		View BuildPhoneLayout()
		{
			var pageList = BuildPhonePageList();

			_phoneNav = NavigationView(pageList)
				.Title("Comet Gallery");

			return _phoneNav;
		}

		View BuildPhonePageList()
		{
			var items = new List<View>();
			string? lastCategory = null;

			for (int i = 0; i < navItems.Count; i++)
			{
				var item = navItems[i];
				var capturedIndex = i;

				if (item.Category != lastCategory)
				{
					if (lastCategory != null)
					{
						items.Add(
							new ShapeView(new Rectangle())
								.Background(Colors.Grey)
								.Frame(height: 1)
								.Opacity(0.15f)
						);
					}
					lastCategory = item.Category;
					items.Add(
						Text(item.Category)
							.FontSize(12)
							.FontWeight(FontWeight.Semibold)
							.Color(CategoryHeaderColor)
							.Padding(new Thickness(16, 12, 16, 4))
					);
				}

				items.Add(
					HStack(12,
						Text(item.Title)
							.FontSize(16)
							.Color(ItemTextColor),
						new Spacer(),
						Text("\u203A")
							.FontSize(18)
							.Color(Colors.Grey)
					)
					.Padding(new Thickness(16, 12))
					.OnTap((v) =>
					{
						var detail = navItems[capturedIndex].CreatePage()
							.Title(navItems[capturedIndex].Title);
						_phoneNav?.Navigate(detail);
					})
				);
			}

			return ScrollView(
				VStack((float?)0, items.ToArray())
			);
		}

		View BuildSidebar()
		{
			var items = new List<View>();
			string lastCategory = null;

			for (int i = 0; i < navItems.Count; i++)
			{
				var item = navItems[i];
				var index = i;

				// Add a subtle separator between category groups (no header text)
				if (item.Category != lastCategory)
				{
					lastCategory = item.Category;
					if (i > 0)
					{
						items.Add(
							new ShapeView(new Rectangle())
								.Background(Colors.Grey)
								.Frame(height: 1)
								.Opacity(0.2f)
						);
					}
				}

				var isSelected = selectedIndex.Value == index;
				var itemColor = isSelected ? SelectedAccent : ItemTextColor;

				items.Add(
					HStack(8,
						Text(item.Title)
							.FontSize(14)
							.HorizontalTextAlignment(TextAlignment.Start)
							.Color(itemColor)
					)
					.Padding(new Thickness(20, 10))
					.Frame(height: 40)
					.Background(isSelected ? SelectedAccent.WithAlpha(0.1f) : SidebarBackground)
					.OnTap((v) =>
					{
						_mainNav?.PopToRoot();
						selectedIndex.Value = index;
					})
				);
			}

			return ScrollView(
				VStack((float?)0, items.ToArray())
			)
			.Background(SidebarBackground);
		}
	}
}
