using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample.Pages;

public class MenuBarPage : ContentPage
{
	readonly Label _statusLabel;
	readonly Label _countLabel;
	int _customMenuCount;

	public MenuBarPage()
	{
		Title = "Menu Bar";

		_statusLabel = new Label
		{
			Text = "Use buttons below to add/remove menu bar items at runtime.",
			FontSize = 14,
		}.WithSecondaryText();

		var addMenuButton = new Button
		{
			Text = "Add Custom Menu",
			BackgroundColor = AppColors.AccentBlue,
			TextColor = Colors.White,
		};
		addMenuButton.Clicked += OnAddMenu;

		var addMenuWithSubButton = new Button
		{
			Text = "Add Menu with Submenu",
			BackgroundColor = AppColors.AccentPurple,
			TextColor = Colors.White,
		};
		addMenuWithSubButton.Clicked += OnAddMenuWithSub;

		var addMenuWithAccelerators = new Button
		{
			Text = "Add Menu with Keyboard Shortcuts",
			BackgroundColor = AppColors.AccentTeal,
			TextColor = Colors.White,
		};
		addMenuWithAccelerators.Clicked += OnAddMenuWithAccelerators;

		var clearMenusButton = new Button
		{
			Text = "Clear All Custom Menus",
			BackgroundColor = AppColors.AccentRed,
			TextColor = Colors.White,
		};
		clearMenusButton.Clicked += OnClearMenus;

		var overrideEditButton = new Button
		{
			Text = "Override Edit Menu",
			BackgroundColor = AppColors.AccentOrange,
			TextColor = Colors.White,
		};
		overrideEditButton.Clicked += OnOverrideEdit;

		var currentMenusLabel = new Label
		{
			Text = "Current custom menus: 0",
			FontSize = 14,
		}.WithPrimaryText();
		_countLabel = currentMenusLabel;

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(24),
				Spacing = 16,
				Children =
				{
					new Label
					{
						Text = "Menu Bar Demo",
						FontSize = 28,
						FontAttributes = FontAttributes.Bold,
					}.WithPrimaryText(),

					new Label
					{
						Text = "This page demonstrates runtime editing of the macOS menu bar. " +
							"The default App, Edit, and Window menus are set up automatically. " +
							"Use the buttons below to add, modify, or clear custom menus.",
						FontSize = 14,
					}.WithSecondaryText(),

					CreateInfoCard(),

					SectionHeader("Add Menus"),
					addMenuButton,
					addMenuWithSubButton,
					addMenuWithAccelerators,

					SectionHeader("Modify Menus"),
					overrideEditButton,
					clearMenusButton,

					SectionHeader("Status"),
					currentMenusLabel,
					_statusLabel,
				}
			}
		};
	}

	static View CreateInfoCard() => new Border
	{
		Stroke = AppColors.AccentBlue,
		StrokeThickness = 1,
		StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
		Padding = new Thickness(16),
		Content = new VerticalStackLayout
		{
			Spacing = 6,
			Children =
			{
				new Label { Text = "How It Works", FontSize = 16, FontAttributes = FontAttributes.Bold }.WithPrimaryText(),
				new Label { Text = "• Page.MenuBarItems maps to the native NSMenu via MenuBarManager", FontSize = 13 }.WithSecondaryText(),
				new Label { Text = "• Default Edit & Window menus are preserved unless you override them by title", FontSize = 13 }.WithSecondaryText(),
				new Label { Text = "• MenuFlyoutItem, MenuFlyoutSubItem, and MenuFlyoutSeparator are supported", FontSize = 13 }.WithSecondaryText(),
				new Label { Text = "• Keyboard accelerators map to native ⌘/⌥/⇧/⌃ shortcuts", FontSize = 13 }.WithSecondaryText(),
			}
		}
	};

	void OnAddMenu(object? sender, EventArgs e)
	{
		_customMenuCount++;
		var menu = new MenuBarItem { Text = $"Custom {_customMenuCount}" };

		for (int i = 1; i <= 3; i++)
		{
			var item = new MenuFlyoutItem { Text = $"Action {i}" };
			var capturedMenu = _customMenuCount;
			var capturedItem = i;
			item.Clicked += (s, args) =>
			{
				_statusLabel.Text = $"Clicked: Custom {capturedMenu} → Action {capturedItem}";
			};
			menu.Add(item);
		}

		MenuBarItems.Add(menu);
		UpdateCount();
		_statusLabel.Text = $"Added menu: Custom {_customMenuCount}";
	}

	void OnAddMenuWithSub(object? sender, EventArgs e)
	{
		_customMenuCount++;
		var menu = new MenuBarItem { Text = $"Custom {_customMenuCount}" };

		var sub = new MenuFlyoutSubItem { Text = "More Options" };
		for (int i = 1; i <= 3; i++)
		{
			var item = new MenuFlyoutItem { Text = $"Sub-action {i}" };
			var capturedItem = i;
			item.Clicked += (s, args) =>
			{
				_statusLabel.Text = $"Clicked: Sub-action {capturedItem}";
			};
			sub.Add(item);
		}

		var topAction = new MenuFlyoutItem { Text = "Top-Level Action" };
		topAction.Clicked += (s, args) => _statusLabel.Text = "Clicked: Top-Level Action";
		menu.Add(topAction);
		menu.Add(new MenuFlyoutSeparator());
		menu.Add(sub);

		MenuBarItems.Add(menu);
		UpdateCount();
		_statusLabel.Text = $"Added menu with submenu: Custom {_customMenuCount}";
	}

	void OnAddMenuWithAccelerators(object? sender, EventArgs e)
	{
		_customMenuCount++;
		var menu = new MenuBarItem { Text = $"Shortcuts {_customMenuCount}" };

		var item1 = new MenuFlyoutItem { Text = "Action ⌘1" };
		item1.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = "1" });
		item1.Clicked += (s, args) => _statusLabel.Text = "Shortcut ⌘1 triggered";
		menu.Add(item1);

		var item2 = new MenuFlyoutItem { Text = "Action ⇧⌘2" };
		item2.KeyboardAccelerators.Add(new KeyboardAccelerator
		{
			Key = "2",
			Modifiers = KeyboardAcceleratorModifiers.Shift,
		});
		item2.Clicked += (s, args) => _statusLabel.Text = "Shortcut ⇧⌘2 triggered";
		menu.Add(item2);

		var item3 = new MenuFlyoutItem { Text = "Action ⌥⌘3" };
		item3.KeyboardAccelerators.Add(new KeyboardAccelerator
		{
			Key = "3",
			Modifiers = KeyboardAcceleratorModifiers.Alt,
		});
		item3.Clicked += (s, args) => _statusLabel.Text = "Shortcut ⌥⌘3 triggered";
		menu.Add(item3);

		MenuBarItems.Add(menu);
		UpdateCount();
		_statusLabel.Text = $"Added menu with shortcuts: Shortcuts {_customMenuCount}";
	}

	void OnClearMenus(object? sender, EventArgs e)
	{
		MenuBarItems.Clear();
		_customMenuCount = 0;
		UpdateCount();
		_statusLabel.Text = "All custom menus cleared. Default Edit & Window menus remain.";
	}

	void OnOverrideEdit(object? sender, EventArgs e)
	{
		// Remove any existing custom "Edit" menu first
		var existing = MenuBarItems.FirstOrDefault(m => m.Text == "Edit");
		if (existing != null)
			MenuBarItems.Remove(existing);

		var editMenu = new MenuBarItem { Text = "Edit" };

		var customUndo = new MenuFlyoutItem { Text = "Custom Undo" };
		customUndo.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = "z" });
		customUndo.Clicked += (s, args) => _statusLabel.Text = "Custom Undo clicked!";
		editMenu.Add(customUndo);

		editMenu.Add(new MenuFlyoutSeparator());

		var findItem = new MenuFlyoutItem { Text = "Find…" };
		findItem.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = "f" });
		findItem.Clicked += (s, args) => _statusLabel.Text = "Find clicked!";
		editMenu.Add(findItem);

		var replaceItem = new MenuFlyoutItem { Text = "Replace…" };
		replaceItem.KeyboardAccelerators.Add(new KeyboardAccelerator
		{
			Key = "f",
			Modifiers = KeyboardAcceleratorModifiers.Shift,
		});
		replaceItem.Clicked += (s, args) => _statusLabel.Text = "Replace clicked!";
		editMenu.Add(replaceItem);

		MenuBarItems.Add(editMenu);
		UpdateCount();
		_statusLabel.Text = "Edit menu overridden with custom items (Find, Replace).";
	}

	void UpdateCount()
	{
		_countLabel.Text = $"Current custom menus: {MenuBarItems.Count}";
		// MenuBarItems is a plain List — mutating it doesn't fire property change.
		// Force the handler to re-read the collection and rebuild the native menu.
		Handler?.UpdateValue(nameof(ContentPage.MenuBarItems));
	}

	static Label SectionHeader(string text) => new Label
	{
		Text = text,
		FontSize = 22,
		FontAttributes = FontAttributes.Bold,
	}.WithSectionStyle();
}
