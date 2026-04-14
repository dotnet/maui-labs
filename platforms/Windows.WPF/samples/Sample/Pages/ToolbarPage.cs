using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
#if WPFAPP
using System.Linq;
#endif

namespace Sample.Pages;

public class ToolbarPage : ContentPage
{
	readonly Label _statusLabel;
	readonly Label _countLabel;
	readonly VerticalStackLayout _itemsList;
	readonly Label _searchStatusLabel;
	int _itemCount;

	public ToolbarPage()
	{
		Title = "Toolbar";

		_statusLabel = new Label
		{
			Text = "Use buttons below to add, remove, and configure toolbar items.",
			FontSize = 14,
		}.WithSecondaryText();

		_countLabel = new Label
		{
			Text = "Current toolbar items: 0",
			FontSize = 14,
		}.WithPrimaryText();

		_itemsList = new VerticalStackLayout { Spacing = 4 };

		_searchStatusLabel = new Label
		{
			Text = "Search: (none)",
			FontSize = 14,
		}.WithSecondaryText();

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
						Text = "Toolbar Demo",
						FontSize = 28,
						FontAttributes = FontAttributes.Bold,
					}.WithPrimaryText(),

					new Label
					{
						Text = "Test adding, removing, and configuring toolbar items at runtime. " +
							"Items can be placed in the content area (default) or the sidebar titlebar area " +
							"when using a native sidebar layout.",
						FontSize = 14,
					}.WithSecondaryText(),

					CreateInfoCard(),

					SectionHeader("Status"),
					_countLabel,
					_statusLabel,

					SectionHeader("Add Items"),
					CreateAddButtons(),

					SectionHeader("Add with SF Symbol Icons"),
					CreateIconButtons(),

#if false
					SectionHeader("Sidebar Placement (macOS)"),
					CreateSidebarPlacementButtons(),
#endif

#if false
					SectionHeader("Explicit Layout (macOS)"),
					CreateExplicitLayoutButtons(),
#endif

#if false
					SectionHeader("Search Toolbar (macOS)"),
					CreateSearchButtons(),

					SectionHeader("Menu Toolbar Item (macOS)"),
					CreateMenuButtons(),

					SectionHeader("Segmented Control / Group (macOS)"),
					CreateGroupButtons(),

					SectionHeader("Share & PopUp (macOS)"),
					CreateSharePopUpButtons(),

					SectionHeader("Item Properties (macOS)"),
					CreateItemPropertyButtons(),

					SectionHeader("System Toolbar Items (macOS)"),
					CreateSystemItemButtons(),

					SectionHeader("Custom View (macOS)"),
					CreateCustomViewButtons(),
#endif

					SectionHeader("Manage Items"),
					CreateManageButtons(),

					SectionHeader("Open in Sidebar Window"),
					CreateSidebarWindowButtons(),

					SectionHeader("Current Items"),
					_itemsList,
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
				new Label { Text = "• Page.ToolbarItems maps to NSToolbarItems in the window toolbar", FontSize = 13 }.WithSecondaryText(),
				new Label { Text = "• Items can show text, SF Symbol icons, or both", FontSize = 13 }.WithSecondaryText(),
				new Label { Text = "• MacOSToolbarItem.Placement controls sidebar vs content area placement", FontSize = 13 }.WithSecondaryText(),
				new Label { Text = "• NSTrackingSeparatorToolbarItem divides sidebar from content area", FontSize = 13 }.WithSecondaryText(),
				new Label { Text = "• Enabled/disabled state is reflected in real-time", FontSize = 13 }.WithSecondaryText(),
			}
		}
	};

	View CreateAddButtons()
	{
		var addText = MakeButton("Add Text Item", AppColors.AccentBlue, (s, e) =>
		{
			_itemCount++;
			var captured = _itemCount;
			var item = new ToolbarItem($"Item {captured}", null, () =>
				SetStatus($"Clicked: Item {captured}"));
			ToolbarItems.Add(item);
			RefreshDisplay();
			SetStatus($"Added text item: Item {captured}");
		});

		var addDisabled = MakeButton("Add Disabled Item", AppColors.AccentPurple, (s, e) =>
		{
			_itemCount++;
			var item = new ToolbarItem
			{
				Text = $"Disabled {_itemCount}",
				Command = new Command(() => SetStatus($"Clicked: Disabled {_itemCount}"), () => false),
			};
			ToolbarItems.Add(item);
			RefreshDisplay();
			SetStatus($"Added disabled item: Disabled {_itemCount}");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { addText, addDisabled } };
	}

	View CreateIconButtons()
	{
		var icons = new (string label, string symbol, Color color)[]
		{
			("plus.circle", "plus.circle", AppColors.AccentGreen),
			("trash", "trash", AppColors.AccentRed),
			("square.and.pencil", "square.and.pencil", AppColors.AccentOrange),
			("magnifyingglass", "magnifyingglass", AppColors.AccentTeal),
			("paperplane", "paperplane", AppColors.AccentBlue),
			("star.fill", "star.fill", AppColors.AccentPink),
		};

		var grid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Star),
			},
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
			},
			ColumnSpacing = 8,
			RowSpacing = 8,
		};

		for (int i = 0; i < icons.Length; i++)
		{
			var (label, symbol, color) = icons[i];
			var captured = symbol;
			var btn = MakeButton(label, color, (s, e) =>
			{
				_itemCount++;
				var item = new ToolbarItem
				{
					Text = captured,
					IconImageSource = captured,
				};
				item.Clicked += (_, _) => SetStatus($"Clicked: {captured}");
				ToolbarItems.Add(item);
				RefreshDisplay();
				SetStatus($"Added icon item: {captured}");
			});
			btn.FontSize = 11;
			btn.HorizontalOptions = LayoutOptions.Fill;
			grid.Add(btn, i % 3, i / 3);
		}

		return grid;
	}

#if false
	View CreateSidebarPlacementButtons()
	{
		var addLeading = MakeButton("＋ Leading (left)", AppColors.AccentGreen, (s, e) =>
		{
			_itemCount++;
			var item = new ToolbarItem { Text = $"Lead {_itemCount}", IconImageSource = "plus" };
			item.Clicked += (_, _) => SetStatus($"Clicked: Lead {_itemCount}");
			MacOSToolbarItem.SetPlacement(item, MacOSToolbarItemPlacement.SidebarLeading);
			ToolbarItems.Add(item);
			RefreshDisplay();
			SetStatus($"Added sidebar leading item");
		});

		var addCenter = MakeButton("◉ Center", AppColors.AccentTeal, (s, e) =>
		{
			_itemCount++;
			var item = new ToolbarItem { Text = $"Center {_itemCount}", IconImageSource = "text.aligncenter" };
			item.Clicked += (_, _) => SetStatus($"Clicked: Center {_itemCount}");
			MacOSToolbarItem.SetPlacement(item, MacOSToolbarItemPlacement.SidebarCenter);
			ToolbarItems.Add(item);
			RefreshDisplay();
			SetStatus($"Added sidebar center item");
		});

		var addTrailing = MakeButton("▸ Trailing (right)", AppColors.AccentOrange, (s, e) =>
		{
			_itemCount++;
			var item = new ToolbarItem { Text = $"Trail {_itemCount}", IconImageSource = "line.3.horizontal.decrease" };
			item.Clicked += (_, _) => SetStatus($"Clicked: Trail {_itemCount}");
			MacOSToolbarItem.SetPlacement(item, MacOSToolbarItemPlacement.SidebarTrailing);
			ToolbarItems.Add(item);
			RefreshDisplay();
			SetStatus($"Added sidebar trailing item");
		});

		var addAllThree = MakeButton("Add Leading + Center + Trailing", AppColors.AccentPurple, (s, e) =>
		{
			_itemCount++;
			var lead = new ToolbarItem { Text = "New", IconImageSource = "plus" };
			lead.Clicked += (_, _) => SetStatus("Clicked: New (leading)");
			MacOSToolbarItem.SetPlacement(lead, MacOSToolbarItemPlacement.SidebarLeading);
			ToolbarItems.Add(lead);

			var center = new ToolbarItem { Text = "Title", IconImageSource = "text.aligncenter" };
			center.Clicked += (_, _) => SetStatus("Clicked: Title (center)");
			MacOSToolbarItem.SetPlacement(center, MacOSToolbarItemPlacement.SidebarCenter);
			ToolbarItems.Add(center);

			var trail = new ToolbarItem { Text = "Filter", IconImageSource = "line.3.horizontal.decrease" };
			trail.Clicked += (_, _) => SetStatus("Clicked: Filter (trailing)");
			MacOSToolbarItem.SetPlacement(trail, MacOSToolbarItemPlacement.SidebarTrailing);
			ToolbarItems.Add(trail);

			var content = new ToolbarItem { Text = "Share", IconImageSource = "square.and.arrow.up" };
			content.Clicked += (_, _) => SetStatus("Clicked: Share (content)");
			ToolbarItems.Add(content);

			RefreshDisplay();
			SetStatus("Added leading + center + trailing + content items");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { addLeading, addCenter, addTrailing, addAllThree } };
	}

	View CreateExplicitLayoutButtons()
	{
		var desc = new Label
		{
			Text = "Set an explicit sidebar layout array with full control over item order, " +
				"flexible spaces, fixed spaces, and separators. Overrides the Leading/Center/Trailing API.",
			FontSize = 13,
		}.WithSecondaryText();

		var setLayout = MakeButton("Set: [+] ─ [Filter] ⟷ [Search]", AppColors.AccentBlue, (s, e) =>
		{
			ToolbarItems.Clear();
			_itemCount = 0;

			var addBtn = new ToolbarItem { Text = "Add", IconImageSource = "plus" };
			addBtn.Clicked += (_, _) => SetStatus("Clicked: Add");
			ToolbarItems.Add(addBtn);

			var filterBtn = new ToolbarItem { Text = "Filter", IconImageSource = "line.3.horizontal.decrease" };
			filterBtn.Clicked += (_, _) => SetStatus("Clicked: Filter");
			ToolbarItems.Add(filterBtn);

			var searchBtn = new ToolbarItem { Text = "Search", IconImageSource = "magnifyingglass" };
			searchBtn.Clicked += (_, _) => SetStatus("Clicked: Search");
			ToolbarItems.Add(searchBtn);

			var shareBtn = new ToolbarItem { Text = "Share", IconImageSource = "square.and.arrow.up" };
			shareBtn.Clicked += (_, _) => SetStatus("Clicked: Share (content)");
			ToolbarItems.Add(shareBtn);

			// Explicit layout: [Add] [Separator] [Filter] [FlexSpace] [Search]
			MacOSToolbar.SetSidebarLayout(this, new MacOSToolbarLayoutItem[]
			{
				MacOSToolbarLayoutItem.Item(addBtn),
				MacOSToolbarLayoutItem.Separator,
				MacOSToolbarLayoutItem.Item(filterBtn),
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Item(searchBtn),
			});

			RefreshDisplay();
			SetStatus("Set explicit layout: [Add] | [Filter] ⟷ [Search] — Share goes to content area");
		});

		var setSpaced = MakeButton("Set: ⟷ [Center] ⟷", AppColors.AccentTeal, (s, e) =>
		{
			ToolbarItems.Clear();
			_itemCount = 0;

			var centerBtn = new ToolbarItem { Text = "Title", IconImageSource = "text.aligncenter" };
			centerBtn.Clicked += (_, _) => SetStatus("Clicked: Title");
			ToolbarItems.Add(centerBtn);

			MacOSToolbar.SetSidebarLayout(this, new MacOSToolbarLayoutItem[]
			{
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Item(centerBtn),
				MacOSToolbarLayoutItem.FlexibleSpace,
			});

			RefreshDisplay();
			SetStatus("Set explicit layout: ⟷ [Title] ⟷ — centered in sidebar");
		});

		var setComplex = MakeButton("Set: [+] [Fav] · [Title] · ⟷ [Filter]", AppColors.AccentPurple, (s, e) =>
		{
			ToolbarItems.Clear();
			_itemCount = 0;

			var addBtn = new ToolbarItem { Text = "Add", IconImageSource = "plus" };
			addBtn.Clicked += (_, _) => SetStatus("Clicked: Add");
			ToolbarItems.Add(addBtn);

			var favBtn = new ToolbarItem { Text = "Fav", IconImageSource = "star.fill" };
			favBtn.Clicked += (_, _) => SetStatus("Clicked: Fav");
			ToolbarItems.Add(favBtn);

			var titleBtn = new ToolbarItem { Text = "Notes", IconImageSource = "text.aligncenter" };
			titleBtn.Clicked += (_, _) => SetStatus("Clicked: Notes");
			ToolbarItems.Add(titleBtn);

			var filterBtn = new ToolbarItem { Text = "Filter", IconImageSource = "line.3.horizontal.decrease" };
			filterBtn.Clicked += (_, _) => SetStatus("Clicked: Filter");
			ToolbarItems.Add(filterBtn);

			MacOSToolbar.SetSidebarLayout(this, new MacOSToolbarLayoutItem[]
			{
				MacOSToolbarLayoutItem.Item(addBtn),
				MacOSToolbarLayoutItem.Item(favBtn),
				MacOSToolbarLayoutItem.Space,
				MacOSToolbarLayoutItem.Item(titleBtn),
				MacOSToolbarLayoutItem.Space,
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Item(filterBtn),
			});

			RefreshDisplay();
			SetStatus("Set complex explicit layout with spaces and flex");
		});

		var setBothLayouts = MakeButton("Set Both: Sidebar + Content layouts", AppColors.AccentGreen, (s, e) =>
		{
			ToolbarItems.Clear();
			_itemCount = 0;

			var addBtn = new ToolbarItem { Text = "Add", IconImageSource = "plus" };
			addBtn.Clicked += (_, _) => SetStatus("Clicked: Add");
			ToolbarItems.Add(addBtn);

			var filterBtn = new ToolbarItem { Text = "Filter", IconImageSource = "line.3.horizontal.decrease" };
			filterBtn.Clicked += (_, _) => SetStatus("Clicked: Filter");
			ToolbarItems.Add(filterBtn);

			var shareBtn = new ToolbarItem { Text = "Share", IconImageSource = "square.and.arrow.up" };
			shareBtn.Clicked += (_, _) => SetStatus("Clicked: Share");
			ToolbarItems.Add(shareBtn);

			var settingsBtn = new ToolbarItem { Text = "Settings", IconImageSource = "gear" };
			settingsBtn.Clicked += (_, _) => SetStatus("Clicked: Settings");
			ToolbarItems.Add(settingsBtn);

			// Sidebar: [Add] <flex> [Filter]
			MacOSToolbar.SetSidebarLayout(this, new MacOSToolbarLayoutItem[]
			{
				MacOSToolbarLayoutItem.Item(addBtn),
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Item(filterBtn),
			});

			// Content: [Share] <flex> [Title] <flex> [Settings]
			MacOSToolbar.SetContentLayout(this, new MacOSToolbarLayoutItem[]
			{
				MacOSToolbarLayoutItem.Item(shareBtn),
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Title,
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Item(settingsBtn),
			});

			RefreshDisplay();
			SetStatus("Set both sidebar + content explicit layouts");
		});

		var clearLayout = MakeButton("Clear Explicit Layouts", AppColors.AccentRed, (s, e) =>
		{
			MacOSToolbar.SetSidebarLayout(this, null);
			MacOSToolbar.SetContentLayout(this, null);
			RefreshDisplay();
			SetStatus("Cleared explicit layouts — items now use Placement property");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { desc, setLayout, setSpaced, setComplex, setBothLayouts, clearLayout } };
	}

	View CreateSearchButtons()
	{
		var desc = new Label
		{
			Text = "Add a native macOS search field to the toolbar. Starts as a magnifying glass " +
				"icon and expands into a search field when clicked (like Finder, Notes, Mail).",
			FontSize = 12,
		}.WithSecondaryText();

		var addContentSearch = MakeButton("Add Search to Content Area", AppColors.AccentBlue, (s, e) =>
		{
			var search = new MacOSSearchToolbarItem
			{
				Placeholder = "Search items…",
				Placement = MacOSToolbarItemPlacement.Content,
			};
			search.TextChanged += (_, args) =>
			{
				_searchStatusLabel.Text = $"Search: \"{args.NewTextValue}\"";
			};
			search.SearchCommitted += (_, text) =>
			{
				SetStatus($"Search committed: \"{text}\"");
			};
			search.SearchStarted += (_, _) =>
			{
				SetStatus("Search field expanded");
			};
			search.SearchEnded += (_, _) =>
			{
				SetStatus("Search field collapsed");
				_searchStatusLabel.Text = "Search: (none)";
			};
			MacOSToolbar.SetSearchItem(this, search);
			SetStatus("Added search to content toolbar area");
		});

		var addSidebarSearch = MakeButton("Add Search to Sidebar Area", AppColors.AccentGreen, (s, e) =>
		{
			var search = new MacOSSearchToolbarItem
			{
				Placeholder = "Filter sidebar…",
				Placement = MacOSToolbarItemPlacement.Sidebar,
				PreferredWidth = 150,
			};
			search.TextChanged += (_, args) =>
			{
				_searchStatusLabel.Text = $"Search: \"{args.NewTextValue}\"";
			};
			search.SearchCommitted += (_, text) =>
			{
				SetStatus($"Sidebar search committed: \"{text}\"");
			};
			MacOSToolbar.SetSearchItem(this, search);
			SetStatus("Added search to sidebar toolbar area");
		});

		var addSearchWithLayout = MakeButton("Search + Explicit Layout", AppColors.AccentPurple, (s, e) =>
		{
			// Create a toolbar item and a search item, then use explicit layout to position them
			var addBtn = new ToolbarItem { Text = "Add", IconImageSource = "plus" };
			addBtn.Clicked += (_, _) => SetStatus("Add clicked (with search layout)");
			ToolbarItems.Add(addBtn);

			var search = new MacOSSearchToolbarItem
			{
				Placeholder = "Find…",
				Placement = MacOSToolbarItemPlacement.Content,
			};
			search.TextChanged += (_, args) =>
			{
				_searchStatusLabel.Text = $"Search: \"{args.NewTextValue}\"";
			};
			search.SearchCommitted += (_, text) =>
			{
				SetStatus($"Search committed: \"{text}\"");
			};
			MacOSToolbar.SetSearchItem(this, search);

			MacOSToolbar.SetContentLayout(this, new MacOSToolbarLayoutItem[]
			{
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Title,
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Search(search),
				MacOSToolbarLayoutItem.Item(addBtn),
			});
			RefreshDisplay();
			SetStatus("Added search + add button with explicit content layout");
		});

		var removeSearch = MakeButton("Remove Search", AppColors.AccentRed, (s, e) =>
		{
			MacOSToolbar.SetSearchItem(this, null);
			MacOSToolbar.SetContentLayout(this, null);
			_searchStatusLabel.Text = "Search: (none)";
			SetStatus("Removed search toolbar item");
		});

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children = { desc, addContentSearch, addSidebarSearch, addSearchWithLayout, removeSearch, _searchStatusLabel }
		};
	}

	View CreateMenuButtons()
	{
		var addMenu = MakeButton("Add Sort/Filter Menu", AppColors.AccentBlue, (s, e) =>
		{
			var menu = new MacOSMenuToolbarItem
			{
				Text = "Sort",
				Icon = "arrow.up.arrow.down",
				ShowsIndicator = true,
			};
			menu.Items.Add(new MacOSMenuItem { Text = "Name", Icon = "textformat.abc", IsChecked = true });
			menu.Items.Add(new MacOSMenuItem { Text = "Date Modified", Icon = "calendar" });
			menu.Items.Add(new MacOSMenuItem { Text = "Size", Icon = "internaldrive" });
			menu.Items.Add(new MacOSMenuItem { IsSeparator = true });
			menu.Items.Add(new MacOSMenuItem { Text = "Ascending", IsChecked = true });
			menu.Items.Add(new MacOSMenuItem { Text = "Descending" });

			foreach (var item in menu.Items.Where(i => !i.IsSeparator))
				item.Clicked += (_, _) => SetStatus($"Menu: {item.Text} clicked");

			MacOSToolbar.SetMenuItems(this, new List<MacOSMenuToolbarItem> { menu });
			SetStatus("Added Sort/Filter menu toolbar item");
		});

		var addMenuWithSub = MakeButton("Add Menu with Submenu", AppColors.AccentGreen, (s, e) =>
		{
			var menu = new MacOSMenuToolbarItem { Text = "Actions", Icon = "ellipsis.circle" };
			var subMenu = new MacOSMenuItem { Text = "Export As…", Icon = "square.and.arrow.up" };
			subMenu.SubItems.Add(new MacOSMenuItem { Text = "PDF", Icon = "doc.richtext" });
			subMenu.SubItems.Add(new MacOSMenuItem { Text = "PNG", Icon = "photo" });
			subMenu.SubItems.Add(new MacOSMenuItem { Text = "CSV", Icon = "tablecells" });
			foreach (var sub in subMenu.SubItems)
				sub.Clicked += (_, _) => SetStatus($"Export as {sub.Text}");

			menu.Items.Add(new MacOSMenuItem { Text = "New Folder", Icon = "folder.badge.plus" });
			menu.Items.Add(new MacOSMenuItem { Text = "Duplicate", Icon = "plus.square.on.square" });
			menu.Items.Add(new MacOSMenuItem { IsSeparator = true });
			menu.Items.Add(subMenu);
			menu.Items.Add(new MacOSMenuItem { IsSeparator = true });
			menu.Items.Add(new MacOSMenuItem { Text = "Delete", Icon = "trash" });

			foreach (var item in menu.Items.Where(i => !i.IsSeparator && i.SubItems.Count == 0))
				item.Clicked += (_, _) => SetStatus($"Action: {item.Text}");

			MacOSToolbar.SetMenuItems(this, new List<MacOSMenuToolbarItem> { menu });
			SetStatus("Added Actions menu with submenu");
		});

		var addMenuWithTitle = MakeButton("Add Menu with Icon+Title", AppColors.AccentPurple, (s, e) =>
		{
			var menu = new MacOSMenuToolbarItem
			{
				Text = "My Identity",
				Icon = "apple.logo",
				ShowsTitle = true,
				ShowsIndicator = true,
			};
			menu.Items.Add(new MacOSMenuItem { Text = "Identity A", Icon = "person" });
			menu.Items.Add(new MacOSMenuItem { Text = "Identity B", Icon = "person.2" });
			menu.Items.Add(new MacOSMenuItem { IsSeparator = true });
			menu.Items.Add(new MacOSMenuItem { Text = "Manage…", Icon = "gear" });

			foreach (var item in menu.Items.Where(i => !i.IsSeparator))
				item.Clicked += (_, _) => SetStatus($"Identity: {item.Text}");

			MacOSToolbar.SetMenuItems(this, new List<MacOSMenuToolbarItem> { menu });
			SetStatus("Added icon+title menu toolbar item");
		});

		var clearMenus = MakeButton("Clear Menus", AppColors.AccentRed, (s, e) =>
		{
			MacOSToolbar.SetMenuItems(this, null);
			SetStatus("Cleared menu toolbar items");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { addMenu, addMenuWithSub, addMenuWithTitle, clearMenus } };
	}

	View CreateGroupButtons()
	{
		var addSelectOne = MakeButton("View Mode (SelectOne)", AppColors.AccentBlue, (s, e) =>
		{
			var group = new MacOSToolbarItemGroup
			{
				Label = "View",
				SelectionMode = MacOSToolbarGroupSelectionMode.SelectOne,
				Representation = MacOSToolbarGroupRepresentation.Collapsed,
				SelectedIndex = 0,
			};
			group.Segments.Add(new MacOSToolbarGroupSegment { Icon = "list.bullet", Label = "List" });
			group.Segments.Add(new MacOSToolbarGroupSegment { Icon = "square.grid.2x2", Label = "Grid" });
			group.Segments.Add(new MacOSToolbarGroupSegment { Icon = "rectangle.grid.1x2", Label = "Gallery" });
			group.SelectionChanged += (_, args) => SetStatus($"View mode: segment {args.SelectedIndex}");

			MacOSToolbar.SetItemGroups(this, new List<MacOSToolbarItemGroup> { group });
			SetStatus("Added SelectOne group (view mode switcher)");
		});

		var addMomentary = MakeButton("Text Style (Momentary)", AppColors.AccentGreen, (s, e) =>
		{
			var group = new MacOSToolbarItemGroup
			{
				Label = "Style",
				SelectionMode = MacOSToolbarGroupSelectionMode.Momentary,
				Representation = MacOSToolbarGroupRepresentation.Expanded,
			};
			group.Segments.Add(new MacOSToolbarGroupSegment { Icon = "bold", Label = "Bold" });
			group.Segments.Add(new MacOSToolbarGroupSegment { Icon = "italic", Label = "Italic" });
			group.Segments.Add(new MacOSToolbarGroupSegment { Icon = "underline", Label = "Underline" });
			group.SelectionChanged += (_, args) => SetStatus($"Style: segment {args.SelectedIndex} pressed");

			MacOSToolbar.SetItemGroups(this, new List<MacOSToolbarItemGroup> { group });
			SetStatus("Added Momentary group (text style)");
		});

		var clearGroups = MakeButton("Clear Groups", AppColors.AccentRed, (s, e) =>
		{
			MacOSToolbar.SetItemGroups(this, null);
			SetStatus("Cleared group toolbar items");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { addSelectOne, addMomentary, clearGroups } };
	}

	View CreateSharePopUpButtons()
	{
		var addShare = MakeButton("Add Share Button", AppColors.AccentBlue, (s, e) =>
		{
			var share = new MacOSShareToolbarItem
			{
				Label = "Share",
			};
			share.ShareItemsProvider = () => new object[]
			{
				"Check out this MAUI macOS app!",
				new Uri("https://github.com/shinyorg/mauiplatforms"),
			};
			MacOSToolbar.SetShareItem(this, share);
			SetStatus("Added Share toolbar button");
		});

		var addPopUp = MakeButton("Add Zoom PopUp", AppColors.AccentGreen, (s, e) =>
		{
			var popup = new MacOSPopUpToolbarItem { Width = 100 };
			popup.Items.Add("50%");
			popup.Items.Add("75%");
			popup.Items.Add("100%");
			popup.Items.Add("125%");
			popup.Items.Add("150%");
			popup.Items.Add("200%");
			popup.SelectedIndex = 2; // 100%
			popup.SelectionChanged += (_, idx) => SetStatus($"Zoom: {popup.Items[idx]}");

			MacOSToolbar.SetPopUpItems(this, new List<MacOSPopUpToolbarItem> { popup });
			SetStatus("Added Zoom popup selector");
		});

		var clearSharePopUp = MakeButton("Clear Share & PopUps", AppColors.AccentRed, (s, e) =>
		{
			MacOSToolbar.SetShareItem(this, null);
			MacOSToolbar.SetPopUpItems(this, null);
			SetStatus("Cleared share and popup items");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { addShare, addPopUp, clearSharePopUp } };
	}

	View CreateItemPropertyButtons()
	{
		var addBadge = MakeButton("Add Item with Badge", AppColors.AccentBlue, (s, e) =>
		{
			_itemCount++;
			var item = new ToolbarItem { Text = $"Inbox", IconImageSource = "tray" };
			MacOSToolbarItem.SetBadge(item, "3");
			MacOSToolbarItem.SetVisibilityPriority(item, MacOSToolbarItemVisibilityPriority.High);
			item.Clicked += (_, _) => SetStatus("Inbox clicked");
			ToolbarItems.Add(item);
			RefreshDisplay();
			SetStatus("Added item with badge '3' and high priority");
		});

		var addTinted = MakeButton("Add Tinted Item", AppColors.AccentGreen, (s, e) =>
		{
			_itemCount++;
			var item = new ToolbarItem { Text = "Important", IconImageSource = "exclamationmark.triangle" };
			MacOSToolbarItem.SetBackgroundTintColor(item, Colors.Orange);
			MacOSToolbarItem.SetToolTip(item, "This is an important action!");
			item.Clicked += (_, _) => SetStatus("Important clicked");
			ToolbarItems.Add(item);
			RefreshDisplay();
			SetStatus("Added tinted item with custom tooltip");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { addBadge, addTinted } };
	}

	View CreateSystemItemButtons()
	{
		var desc = new Label
		{
			Text = "Add built-in macOS system toolbar items. These require an explicit layout to position.",
			FontSize = 12,
		}.WithSecondaryText();

		var addSystemItems = MakeButton("Add System Items (Layout)", AppColors.AccentPurple, (s, e) =>
		{
			MacOSToolbar.SetContentLayout(this, new MacOSToolbarLayoutItem[]
			{
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.Title,
				MacOSToolbarLayoutItem.FlexibleSpace,
				MacOSToolbarLayoutItem.ShowColors,
				MacOSToolbarLayoutItem.ShowFonts,
				MacOSToolbarLayoutItem.Print,
			});
			SetStatus("Added Colors, Fonts, Print system items via explicit layout");
		});

		var clearSystem = MakeButton("Clear Layout", AppColors.AccentRed, (s, e) =>
		{
			MacOSToolbar.SetContentLayout(this, null);
			SetStatus("Cleared explicit layout");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { desc, addSystemItems, clearSystem } };
	}
#endif

	View CreateManageButtons()
	{
		var removeLast = MakeButton("Remove Last Item", AppColors.AccentRed, (s, e) =>
		{
			if (ToolbarItems.Count > 0)
			{
				var last = ToolbarItems[ToolbarItems.Count - 1];
				ToolbarItems.Remove(last);
				RefreshDisplay();
				SetStatus($"Removed: {last.Text}");
			}
			else
			{
				SetStatus("No items to remove");
			}
		});

		var removeFirst = MakeButton("Remove First Item", AppColors.AccentOrange, (s, e) =>
		{
			if (ToolbarItems.Count > 0)
			{
				var first = ToolbarItems[0];
				ToolbarItems.RemoveAt(0);
				RefreshDisplay();
				SetStatus($"Removed: {first.Text}");
			}
			else
			{
				SetStatus("No items to remove");
			}
		});

		var clearAll = MakeButton("Clear All Items", AppColors.AccentRed, (s, e) =>
		{
			ToolbarItems.Clear();
			_itemCount = 0;
			RefreshDisplay();
			SetStatus("All toolbar items cleared");
		});

		var toggleFirst = MakeButton("Toggle First Item Enabled", AppColors.AccentTeal, (s, e) =>
		{
			if (ToolbarItems.Count > 0)
			{
				var first = ToolbarItems[0];
				// Toggle via command CanExecute by replacing the command
				if (first.Command is Command cmd)
				{
					var wasEnabled = cmd.CanExecute(null);
					first.Command = new Command(
						() => SetStatus($"Clicked: {first.Text}"),
						() => !wasEnabled);
					((Command)first.Command).ChangeCanExecute();
					RefreshDisplay();
					SetStatus($"Toggled {first.Text}: now {(!wasEnabled ? "enabled" : "disabled")}");
				}
				else
				{
					// Item has no command — give it a disabled one
					first.Command = new Command(
						() => SetStatus($"Clicked: {first.Text}"),
						() => false);
					((Command)first.Command).ChangeCanExecute();
					RefreshDisplay();
					SetStatus($"Disabled {first.Text}");
				}
			}
			else
			{
				SetStatus("No items to toggle");
			}
		});

		var renameFirst = MakeButton("Rename First Item", AppColors.AccentBlue, (s, e) =>
		{
			if (ToolbarItems.Count > 0)
			{
				var first = ToolbarItems[0];
				first.Text = $"Renamed {++_itemCount}";
				RefreshDisplay();
				SetStatus($"Renamed first item to: {first.Text}");
			}
			else
			{
				SetStatus("No items to rename");
			}
		});

		return new VerticalStackLayout
		{
			Spacing = 8,
			Children = { removeLast, removeFirst, clearAll, toggleFirst, renameFirst }
		};
	}

	View CreateSidebarWindowButtons()
	{
		var desc = new Label
		{
			Text = "Open this toolbar page inside a Shell with a native sidebar to test " +
				"the NSTrackingSeparatorToolbarItem behavior — sidebar-placed items appear " +
				"in the sidebar titlebar area.",
			FontSize = 13,
		}.WithSecondaryText();

		var openShell = MakeButton("Open in Shell (Native Sidebar)", AppColors.AccentPurple, (s, e) =>
		{
#if WPFAPP
			var shell = new ToolbarDemoShell();
			Application.Current?.OpenWindow(new Window(shell));
			SetStatus("Opened toolbar demo in Shell with native sidebar");
#else
			SetStatus("Only available on macOS");
#endif
		});

		return new VerticalStackLayout { Spacing = 8, Children = { desc, openShell } };
	}

	void SetStatus(string text)
	{
		_statusLabel.Text = text;
	}

	void RefreshDisplay()
	{
		_countLabel.Text = $"Current toolbar items: {ToolbarItems.Count}";

		_itemsList.Children.Clear();
		for (int i = 0; i < ToolbarItems.Count; i++)
		{
			var item = ToolbarItems[i];
			var placement = "Content";
#if false
			placement = MacOSToolbarItem.GetPlacement(item) switch
			{
				MacOSToolbarItemPlacement.SidebarLeading => "Sidebar/Leading",
				MacOSToolbarItemPlacement.SidebarCenter => "Sidebar/Center",
				MacOSToolbarItemPlacement.SidebarTrailing => "Sidebar/Trailing",
				_ => "Content",
			};
#endif
			var icon = item.IconImageSource is FileImageSource fs ? fs.File : "(none)";
			var enabled = item.Command == null || item.Command.CanExecute(null);

			var label = new Label
			{
				Text = $"  [{i}] \"{item.Text}\"  —  icon: {icon}  |  placement: {placement}  |  enabled: {enabled}",
				FontSize = 12,
				FontFamily = "Courier",
			}.WithSecondaryText();
			_itemsList.Children.Add(label);
		}

		if (ToolbarItems.Count == 0)
		{
			_itemsList.Children.Add(new Label
			{
				Text = "  (no toolbar items)",
				FontSize = 12,
				FontAttributes = FontAttributes.Italic,
			}.WithSecondaryText());
		}
	}

	static Button MakeButton(string text, Color bg, EventHandler handler)
	{
		var btn = new Button
		{
			Text = text,
			BackgroundColor = bg,
			TextColor = Colors.White,
			HeightRequest = 40,
			CornerRadius = 8,
			FontSize = 13,
			HorizontalOptions = LayoutOptions.Start,
			Padding = new Thickness(20, 0),
		};
		btn.Clicked += handler;
		return btn;
	}

	static Label SectionHeader(string text) => new Label
	{
		Text = text,
		FontSize = 22,
		FontAttributes = FontAttributes.Bold,
	}.WithSectionStyle();

#if false
	View CreateCustomViewButtons()
	{
		var addLabel = MakeButton("Add View (no border)", AppColors.AccentBlue, (s, e) =>
		{
			var label = new Label
			{
				Text = "Status: Ready",
				FontSize = 13,
				VerticalTextAlignment = TextAlignment.Center,
				Padding = new Thickness(4, 0),
			};
			var viewItem = new MacOSViewToolbarItem
			{
				Label = "Status",
				View = label,
			};
			MacOSToolbar.SetViewItems(this, new List<MacOSViewToolbarItem> { viewItem });
			SetStatus("Added borderless custom view to toolbar");
		});

		var addStack = MakeButton("Add View (button style)", AppColors.AccentGreen, (s, e) =>
		{
			var progress = new ProgressBar { Progress = 0.6, WidthRequest = 80, VerticalOptions = LayoutOptions.Center };
			var stack = new HorizontalStackLayout
			{
				Spacing = 6,
				Padding = new Thickness(8, 0),
				VerticalOptions = LayoutOptions.Center,
				Children =
				{
					new Label { Text = "Build:", FontSize = 12, VerticalOptions = LayoutOptions.Center, VerticalTextAlignment = TextAlignment.Center },
					progress,
					new Label { Text = "60%", FontSize = 12, VerticalOptions = LayoutOptions.Center, VerticalTextAlignment = TextAlignment.Center },
				}
			};
			var viewItem = new MacOSViewToolbarItem
			{
				Label = "Progress",
				ShowsToolbarButtonStyle = true,
				View = stack,
			};
			viewItem.Clicked += (_, _) => SetStatus("Progress view clicked!");
			MacOSToolbar.SetViewItems(this, new List<MacOSViewToolbarItem> { viewItem });
			SetStatus("Added button-style custom view to toolbar");
		});

		var clearViews = MakeButton("Clear Views", AppColors.AccentRed, (s, e) =>
		{
			MacOSToolbar.SetViewItems(this, null);
			SetStatus("Cleared custom view toolbar items");
		});

		return new VerticalStackLayout { Spacing = 8, Children = { addLabel, addStack, clearViews } };
	}
#endif
}

#if WPFAPP
/// <summary>
/// A Shell that hosts the ToolbarPage with native sidebar, to test
/// sidebar-placed toolbar items with NSTrackingSeparatorToolbarItem.
/// </summary>
class ToolbarDemoShell : Shell
{
	public ToolbarDemoShell()
	{
		Title = "Toolbar Demo — Native Sidebar";
		FlyoutBehavior = FlyoutBehavior.Locked;
#if MACAPP
		MacOSShell.SetUseNativeSidebar(this, true);
#endif

		var main = new FlyoutItem { Title = "Toolbar" };
		var toolbarContent = new ShellContent
		{
			Title = "Toolbar Test",
			Route = "toolbartest",
			ContentTemplate = new DataTemplate(() =>
			{
				var page = new ToolbarTestContentPage("Toolbar Test");
				return page;
			}),
		};
#if MACAPP
		MacOSShell.SetSystemImage(toolbarContent, "hammer");
#endif
		main.Items.Add(toolbarContent);

		var secondContent = new ShellContent
		{
			Title = "Another Page",
			Route = "anotherpage",
			ContentTemplate = new DataTemplate(() =>
			{
				var page = new ToolbarTestContentPage("Another Page");
				return page;
			}),
		};
#if MACAPP
		MacOSShell.SetSystemImage(secondContent, "doc.text");
#endif
		main.Items.Add(secondContent);

		Items.Add(main);
	}
}

/// <summary>
/// A content page with pre-configured sidebar and content toolbar items
/// and buttons to manipulate them at runtime.
/// </summary>
class ToolbarTestContentPage : ContentPage
{
	readonly Label _statusLabel;
	int _counter;

	public ToolbarTestContentPage(string title)
	{
		Title = title;

		_statusLabel = new Label
		{
			Text = "Toolbar items are configured. Try the buttons below.",
			FontSize = 14,
		}.WithSecondaryText();

		// Start with some default toolbar items
		AddDefaultItems();

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
						Text = title,
						FontSize = 28,
						FontAttributes = FontAttributes.Bold,
					}.WithPrimaryText(),

					new Label
					{
						Text = "This page has toolbar items placed in both the sidebar titlebar area " +
							"and the content toolbar area. The tracking separator aligns with the sidebar divider.",
						FontSize = 14,
					}.WithSecondaryText(),

					SectionHeader("Sidebar Area Items"),
					MakeButton("Add Sidebar Item", AppColors.AccentGreen, OnAddSidebarItem),
					MakeButton("Remove Sidebar Items", AppColors.AccentRed, OnRemoveSidebarItems),

					SectionHeader("Content Area Items"),
					MakeButton("Add Content Item", AppColors.AccentBlue, OnAddContentItem),
					MakeButton("Remove Content Items", AppColors.AccentOrange, OnRemoveContentItems),

					SectionHeader("Bulk Operations"),
					MakeButton("Clear All", AppColors.AccentRed, OnClearAll),
					MakeButton("Reset to Defaults", AppColors.AccentPurple, OnResetDefaults),

					SectionHeader("Status"),
					_statusLabel,
				}
			}
		};
	}

	void AddDefaultItems()
	{
		// Sidebar leading
		var newItem = new ToolbarItem { Text = "New", IconImageSource = "plus" };
		newItem.Clicked += (_, _) => _statusLabel.Text = "Clicked: New (leading)";
#if MACAPP
		MacOSToolbarItem.SetPlacement(newItem, MacOSToolbarItemPlacement.SidebarLeading);
#endif
		ToolbarItems.Add(newItem);

		// Sidebar trailing
		var filterItem = new ToolbarItem { Text = "Filter", IconImageSource = "line.3.horizontal.decrease" };
		filterItem.Clicked += (_, _) => _statusLabel.Text = "Clicked: Filter (trailing)";
#if MACAPP
		MacOSToolbarItem.SetPlacement(filterItem, MacOSToolbarItemPlacement.SidebarTrailing);
#endif
		ToolbarItems.Add(filterItem);

		// Content toolbar items
		var shareItem = new ToolbarItem { Text = "Share", IconImageSource = "square.and.arrow.up" };
		shareItem.Clicked += (_, _) => _statusLabel.Text = "Clicked: Share (content)";
		ToolbarItems.Add(shareItem);

		var searchItem = new ToolbarItem { Text = "Search", IconImageSource = "magnifyingglass" };
		searchItem.Clicked += (_, _) => _statusLabel.Text = "Clicked: Search (content)";
		ToolbarItems.Add(searchItem);
	}

	void OnAddSidebarItem(object? s, EventArgs e)
	{
		_counter++;
		var item = new ToolbarItem
		{
			Text = $"Side {_counter}",
			IconImageSource = "folder",
		};
		item.Clicked += (_, _) => _statusLabel.Text = $"Clicked: Side {_counter}";
#if MACAPP
		MacOSToolbarItem.SetPlacement(item, MacOSToolbarItemPlacement.Sidebar);
#endif
		ToolbarItems.Add(item);
		_statusLabel.Text = $"Added sidebar item: Side {_counter}";
	}

	void OnAddContentItem(object? s, EventArgs e)
	{
		_counter++;
		var item = new ToolbarItem
		{
			Text = $"Action {_counter}",
			IconImageSource = "star",
		};
		item.Clicked += (_, _) => _statusLabel.Text = $"Clicked: Action {_counter}";
		ToolbarItems.Add(item);
		_statusLabel.Text = $"Added content item: Action {_counter}";
	}

	void OnRemoveSidebarItems(object? s, EventArgs e)
	{
		var toRemove = ToolbarItems
#if MACAPP
			.Where(i => {
				var p = MacOSToolbarItem.GetPlacement(i);
				return p == MacOSToolbarItemPlacement.SidebarLeading
					|| p == MacOSToolbarItemPlacement.SidebarCenter
					|| p == MacOSToolbarItemPlacement.SidebarTrailing;
			})
#else
			.Where(i => false)
#endif
			.ToList();
		foreach (var item in toRemove)
			ToolbarItems.Remove(item);
		_statusLabel.Text = $"Removed {toRemove.Count} sidebar items";
	}

	void OnRemoveContentItems(object? s, EventArgs e)
	{
		var toRemove = ToolbarItems
#if MACAPP
			.Where(i => MacOSToolbarItem.GetPlacement(i) == MacOSToolbarItemPlacement.Content)
#else
			.Where(i => false)
#endif
			.ToList();
		foreach (var item in toRemove)
			ToolbarItems.Remove(item);
		_statusLabel.Text = $"Removed {toRemove.Count} content items";
	}

	void OnClearAll(object? s, EventArgs e)
	{
		ToolbarItems.Clear();
		_counter = 0;
		_statusLabel.Text = "All toolbar items cleared";
	}

	void OnResetDefaults(object? s, EventArgs e)
	{
		ToolbarItems.Clear();
		_counter = 0;
		AddDefaultItems();
		_statusLabel.Text = "Reset to default toolbar items";
	}

	static Button MakeButton(string text, Color bg, EventHandler handler)
	{
		var btn = new Button
		{
			Text = text,
			BackgroundColor = bg,
			TextColor = Colors.White,
			HeightRequest = 40,
			CornerRadius = 8,
			FontSize = 13,
			HorizontalOptions = LayoutOptions.Start,
			Padding = new Thickness(20, 0),
		};
		btn.Clicked += handler;
		return btn;
	}

	static Label SectionHeader(string text) => new Label
	{
		Text = text,
		FontSize = 22,
		FontAttributes = FontAttributes.Bold,
	}.WithSectionStyle();
}
#endif


