using System;
using System.Linq;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class MenuBarState
	{
		public int CustomMenuCount { get; set; }
		public string StatusText { get; set; } = "Use buttons below to add/remove menu bar items at runtime.";
		public Color StatusColor { get; set; } = Colors.Grey;
	}

	// Note: MenuBarItems are a ContentPage feature in MAUI. In Comet's MVU model,
	// views don't directly own MenuBarItems. This demo shows the menu bar concepts
	// and documents the approach for adding menus via the underlying MAUI page.
	public class MenuBarPage : Component<MenuBarState>
	{
		public override View Render() => GalleryPageHelpers.Scaffold("Menu Bar",
			// Title
			Text("Menu Bar Demo")
				.FontSize(28)
				.FontWeight(FontWeight.Bold),

			Text("This page demonstrates runtime editing of the macOS menu bar. "
				+ "The default App, Edit, and Window menus are set up automatically. "
				+ "Use the buttons below to add, modify, or clear custom menus.")
				.FontSize(14)
				.Color(Colors.Grey),

			// Info card
			Border(
				VStack(6,
					Text("How It Works")
						.FontSize(16)
						.FontWeight(FontWeight.Bold),
					Text("• Page.MenuBarItems maps to the native NSMenu via MenuBarManager")
						.FontSize(13)
						.Color(Colors.Grey),
					Text("• Default Edit & Window menus are preserved unless you override them by title")
						.FontSize(13)
						.Color(Colors.Grey),
					Text("• MenuFlyoutItem, MenuFlyoutSubItem, and MenuFlyoutSeparator are supported")
						.FontSize(13)
						.Color(Colors.Grey),
					Text("• Keyboard accelerators map to native ⌘/⌥/⇧/⌃ shortcuts")
						.FontSize(13)
						.Color(Colors.Grey)
				)
				.Padding(new Thickness(16))
			)
			.StrokeColor(Colors.CornflowerBlue)
			.StrokeThickness(1)
			.CornerRadius(8),

			// Add Menus section
			GalleryPageHelpers.Section("Add Menus",
				Button("Add Custom Menu", AddMenu)
					.Background(Colors.CornflowerBlue)
					.Color(Colors.White),
				Button("Add Menu with Submenu", AddMenuWithSub)
					.Background(new Color(123, 104, 238))
					.Color(Colors.White),
				Button("Add Menu with Keyboard Shortcuts", AddMenuWithAccelerators)
					.Background(Colors.Teal)
					.Color(Colors.White)
			),

			// Modify Menus section
			GalleryPageHelpers.Section("Modify Menus",
				Button("Override Edit Menu", OverrideEditMenu)
					.Background(Colors.Orange)
					.Color(Colors.White),
				Button("Clear All Custom Menus", ClearMenus)
					.Background(Colors.Red)
					.Color(Colors.White)
			),

			// Status section
			GalleryPageHelpers.Section("Status",
				Text(() => $"Current custom menus: {State.CustomMenuCount}")
					.FontSize(14),
				Text(() => State.StatusText)
					.FontSize(14)
					.Color(() => State.StatusColor)
			)
		);

		Microsoft.Maui.Controls.ContentPage GetPage()
		{
			var window = Microsoft.Maui.Controls.Application.Current?.Windows?.Count > 0
				? Microsoft.Maui.Controls.Application.Current.Windows[0]
				: null;
			return window?.Page as Microsoft.Maui.Controls.ContentPage;
		}

		void AddMenu()
		{
			var page = GetPage();
			if (page is null) return;

			SetState(s => s.CustomMenuCount++);
			var count = State.CustomMenuCount;
			var menu = new Microsoft.Maui.Controls.MenuBarItem { Text = $"Custom {count}" };

			for (int i = 1; i <= 3; i++)
			{
				var capturedItem = i;
				var item = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = $"Action {i}" };
				item.Clicked += (s, args) =>
					SetState(st => { st.StatusText = $"Clicked: Custom {count} > Action {capturedItem}"; st.StatusColor = Colors.DodgerBlue; });
				menu.Add(item);
			}

			page.MenuBarItems.Add(menu);
			page.Handler?.UpdateValue(nameof(Microsoft.Maui.Controls.ContentPage.MenuBarItems));
			SetState(s => { s.StatusText = $"Added menu: Custom {count}"; s.StatusColor = Colors.Green; });
		}

		void AddMenuWithSub()
		{
			var page = GetPage();
			if (page is null) return;

			SetState(s => s.CustomMenuCount++);
			var count = State.CustomMenuCount;
			var menu = new Microsoft.Maui.Controls.MenuBarItem { Text = $"Custom {count}" };

			var topAction = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = "Top-Level Action" };
			topAction.Clicked += (s, args) =>
				SetState(st => { st.StatusText = "Clicked: Top-Level Action"; st.StatusColor = Colors.DodgerBlue; });
			menu.Add(topAction);
			menu.Add(new Microsoft.Maui.Controls.MenuFlyoutSeparator());

			var sub = new Microsoft.Maui.Controls.MenuFlyoutSubItem { Text = "More Options" };
			for (int i = 1; i <= 3; i++)
			{
				var capturedItem = i;
				var item = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = $"Sub-action {i}" };
				item.Clicked += (s, args) =>
					SetState(st => { st.StatusText = $"Clicked: Sub-action {capturedItem}"; st.StatusColor = Colors.DodgerBlue; });
				sub.Add(item);
			}
			menu.Add(sub);

			page.MenuBarItems.Add(menu);
			page.Handler?.UpdateValue(nameof(Microsoft.Maui.Controls.ContentPage.MenuBarItems));
			SetState(s => { s.StatusText = $"Added menu with submenu: Custom {count}"; s.StatusColor = Colors.Green; });
		}

		void AddMenuWithAccelerators()
		{
			var page = GetPage();
			if (page is null) return;

			SetState(s => s.CustomMenuCount++);
			var count = State.CustomMenuCount;
			var menu = new Microsoft.Maui.Controls.MenuBarItem { Text = $"Shortcuts {count}" };

			var item1 = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = "Action 1" };
			item1.Clicked += (s, args) =>
				SetState(st => { st.StatusText = "Shortcut 1 triggered"; st.StatusColor = Colors.DodgerBlue; });
			menu.Add(item1);

			var item2 = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = "Action 2" };
			item2.Clicked += (s, args) =>
				SetState(st => { st.StatusText = "Shortcut 2 triggered"; st.StatusColor = Colors.DodgerBlue; });
			menu.Add(item2);

			var item3 = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = "Action 3" };
			item3.Clicked += (s, args) =>
				SetState(st => { st.StatusText = "Shortcut 3 triggered"; st.StatusColor = Colors.DodgerBlue; });
			menu.Add(item3);

			page.MenuBarItems.Add(menu);
			page.Handler?.UpdateValue(nameof(Microsoft.Maui.Controls.ContentPage.MenuBarItems));
			SetState(s => { s.StatusText = $"Added menu with shortcuts: Shortcuts {count}"; s.StatusColor = Colors.Green; });
		}

		void OverrideEditMenu()
		{
			var page = GetPage();
			if (page is null) return;

			Microsoft.Maui.Controls.MenuBarItem existing = null;
			foreach (var m in page.MenuBarItems)
			{
				if (m.Text == "Edit") { existing = m; break; }
			}
			if (existing != null)
				page.MenuBarItems.Remove(existing);

			var editMenu = new Microsoft.Maui.Controls.MenuBarItem { Text = "Edit" };

			var customUndo = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = "Custom Undo" };
			customUndo.Clicked += (s, args) =>
				SetState(st => { st.StatusText = "Custom Undo clicked!"; st.StatusColor = Colors.DodgerBlue; });
			editMenu.Add(customUndo);

			editMenu.Add(new Microsoft.Maui.Controls.MenuFlyoutSeparator());

			var findItem = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = "Find..." };
			findItem.Clicked += (s, args) =>
				SetState(st => { st.StatusText = "Find clicked!"; st.StatusColor = Colors.DodgerBlue; });
			editMenu.Add(findItem);

			var replaceItem = new Microsoft.Maui.Controls.MenuFlyoutItem { Text = "Replace..." };
			replaceItem.Clicked += (s, args) =>
				SetState(st => { st.StatusText = "Replace clicked!"; st.StatusColor = Colors.DodgerBlue; });
			editMenu.Add(replaceItem);

			page.MenuBarItems.Add(editMenu);
			page.Handler?.UpdateValue(nameof(Microsoft.Maui.Controls.ContentPage.MenuBarItems));
			SetState(s => { s.StatusText = "Edit menu overridden with custom items (Find, Replace)."; s.StatusColor = Colors.Green; });
		}

		void ClearMenus()
		{
			var page = GetPage();
			if (page is null) return;

			page.MenuBarItems.Clear();
			page.Handler?.UpdateValue(nameof(Microsoft.Maui.Controls.ContentPage.MenuBarItems));
			SetState(s =>
			{
				s.CustomMenuCount = 0;
				s.StatusText = "All custom menus cleared. Default Edit & Window menus remain.";
				s.StatusColor = Colors.OrangeRed;
			});
		}
	}
}
