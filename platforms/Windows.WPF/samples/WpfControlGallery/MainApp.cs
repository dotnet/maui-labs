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

		var menuItems = _pages.Select(p => p.name).ToList();
		var menuList = new CollectionView
		{
			ItemsSource = menuItems,
			SelectionMode = SelectionMode.Single,
			SelectedItem = menuItems[0],
			VerticalOptions = LayoutOptions.Fill,
			Header = "WPF Demo",
		};

		menuList.SelectionChanged += (s, e) =>
		{
			if (e.CurrentSelection.FirstOrDefault() is string selected)
			{
				var match = _pages.FirstOrDefault(p => p.name == selected);
				if (match.factory != null)
				{
					var page = match.factory();
					if (page is ContentPage cp)
						Detail = new NavigationPage(cp);
					else
						Detail = page;
				}
			}
		};

		Flyout = new ContentPage
		{
			Title = "Menu",
			Content = menuList,
		};

		Detail = new NavigationPage(new HomePage());
		IsPresented = true;
	}
}
