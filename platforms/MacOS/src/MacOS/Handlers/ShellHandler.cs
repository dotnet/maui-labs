using System.Collections.Specialized;
using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AppKit;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Shell handler for macOS. Renders Shell as a split view with:
/// - Left sidebar (flyout) showing Shell items
/// - Right content area showing the current page
/// On macOS, the flyout is always visible (like a source list sidebar).
/// </summary>
public partial class ShellHandler : ViewHandler<Shell, NSView>
{
	public static readonly IPropertyMapper<Shell, ShellHandler> Mapper =
		new PropertyMapper<Shell, ShellHandler>(ViewMapper)
		{
			[nameof(Shell.CurrentItem)] = MapCurrentItem,
			[nameof(Shell.FlyoutBackgroundColor)] = MapFlyoutBackground,
			[nameof(Shell.FlyoutHeaderTemplate)] = MapFlyoutHeader,
			[nameof(Shell.FlyoutHeader)] = MapFlyoutHeader,
			[nameof(Shell.Items)] = MapItems,
			[nameof(Shell.FlyoutItems)] = MapFlyoutItems,
			[nameof(IFlyoutView.FlyoutBehavior)] = MapFlyoutBehavior,
			[nameof(IFlyoutView.IsPresented)] = MapIsPresented,
			[nameof(IFlyoutView.FlyoutWidth)] = MapFlyoutWidth,
		};

	public static readonly CommandMapper<Shell, ShellHandler> CommandMapper =
		new(ViewCommandMapper);

	NSSplitViewController? _splitViewController;
	NSSplitViewItem? _sidebarSplitItem;
	NSView? _sidebarView;
	NSView? _contentView;
	NSObject? _contentFrameChangedObserver;
	NSView? _currentPageView;
	Page? _currentPage;
	Shell? _shell;
	nfloat _flyoutWidth = 300;

	// Custom sidebar mode
	NSScrollView? _sidebarScrollView;
	FlippedDocumentView? _sidebarContent;

	// Native sidebar mode (NSOutlineView source list)
	bool _useNativeSidebar;
	NSScrollView? _nativeSidebarScrollView;
	NSOutlineView? _outlineView;
	SidebarOutlineViewDataSource? _outlineDataSource;
	SidebarOutlineViewDelegate? _outlineDelegate;
	// Maps leaf MacOSSidebarItem → (ShellItem, ShellSection, ShellContent)
	Dictionary<MacOSSidebarItem, (ShellItem, ShellSection, ShellContent)>? _itemNavMap;
	bool _isUpdatingSelection;

	public ShellHandler() : base(Mapper, CommandMapper)
	{
	}

	/// <summary>
	/// Exposes the NSSplitViewController so WindowHandler can set it as the
	/// window's contentViewController for proper sidebar titlebar integration.
	/// </summary>
	internal NSSplitViewController? SplitViewController => _splitViewController;

	protected override NSView CreatePlatformView()
	{
		_useNativeSidebar = VirtualView is Shell shell && MacOSShell.GetUseNativeSidebar(shell);

		// Sidebar — use NSVisualEffectView for behind-window vibrancy
		// (translucent background that blends with content behind the window)
		_sidebarView = new NSVisualEffectView
		{
			BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
			Material = NSVisualEffectMaterial.Sidebar,
			State = NSVisualEffectState.FollowsWindowActiveState,
		};

		if (_useNativeSidebar)
		{
			// Native NSOutlineView source list sidebar
			_outlineView = new NSOutlineView
			{
				SelectionHighlightStyle = NSTableViewSelectionHighlightStyle.SourceList,
				FloatsGroupRows = false,
				RowSizeStyle = NSTableViewRowSizeStyle.Default,
				HeaderView = null,
			};

			var column = new NSTableColumn("SidebarColumn")
			{
				Editable = false,
			};
			_outlineView.AddColumn(column);
			_outlineView.OutlineTableColumn = column;

			_nativeSidebarScrollView = new NSScrollView
			{
				HasVerticalScroller = true,
				HasHorizontalScroller = false,
				AutohidesScrollers = true,
				DrawsBackground = false,
				DocumentView = _outlineView,
				AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
			};
			_sidebarView.AddSubview(_nativeSidebarScrollView);
		}
		else
		{
			// Custom sidebar with MAUI-drawn items
			_sidebarScrollView = new NSScrollView
			{
				HasVerticalScroller = true,
				HasHorizontalScroller = false,
				AutohidesScrollers = true,
				DrawsBackground = false,
				AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
			};

			_sidebarContent = new FlippedDocumentView();
			_sidebarScrollView.DocumentView = _sidebarContent;
			_sidebarView.AddSubview(_sidebarScrollView);
		}

		// Content area — observe frame changes to re-layout MAUI content
		_contentView = new FlippedDocumentView();
		_contentView.WantsLayer = true;
		_contentView.Layer!.MasksToBounds = false;
		_contentView.PostsFrameChangedNotifications = true;
		_contentFrameChangedObserver = NSNotificationCenter.DefaultCenter.AddObserver(
			NSView.FrameChangedNotification, OnContentFrameChanged, _contentView);

		// Use NSSplitViewController for native inset sidebar appearance
		_splitViewController = new NSSplitViewController();

		var sidebarVC = new NSViewController();
		sidebarVC.View = _sidebarView;

		var contentVC = new NSViewController();
		contentVC.View = _contentView;

		_sidebarSplitItem = NSSplitViewItem.CreateSidebar(sidebarVC);
		_sidebarSplitItem.MinimumThickness = 150;
		_sidebarSplitItem.MaximumThickness = 400;
		_sidebarSplitItem.PreferredThicknessFraction = 0;
		_sidebarSplitItem.CanCollapse = false;
		_sidebarSplitItem.AllowsFullHeightLayout = true;
		_sidebarSplitItem.TitlebarSeparatorStyle = NSTitlebarSeparatorStyle.None;

		// Set initial sidebar width via a width constraint on the sidebar view
		_sidebarView.SetFrameSize(new CoreGraphics.CGSize(_flyoutWidth, _sidebarView.Frame.Height));
		var widthConstraint = _sidebarView.WidthAnchor.ConstraintEqualTo(_flyoutWidth);
		widthConstraint.Priority = 200; // low priority so user can still resize
		widthConstraint.Active = true;

		var contentItem = NSSplitViewItem.CreateContentList(contentVC);
		contentItem.AllowsFullHeightLayout = true;
		contentItem.TitlebarSeparatorStyle = NSTitlebarSeparatorStyle.Line;

		_splitViewController.AddSplitViewItem(_sidebarSplitItem);
		_splitViewController.AddSplitViewItem(contentItem);

		// Apply resize constraint
		if (VirtualView is Shell s && !MacOSShell.GetIsSidebarResizable(s))
		{
			_sidebarSplitItem.MinimumThickness = _flyoutWidth;
			_sidebarSplitItem.MaximumThickness = _flyoutWidth;
		}

		return _splitViewController.View;
	}

	protected override void ConnectHandler(NSView platformView)
	{
		base.ConnectHandler(platformView);
		_shell = VirtualView;

		if (_shell != null)
		{
			((INotifyCollectionChanged)_shell.Items).CollectionChanged += OnShellItemsChanged;
			_shell.Navigating += OnShellNavigating;
			_shell.Navigated += OnShellNavigated;
			_shell.PropertyChanged += OnShellPropertyChanged;

			// Ensure handlers are created for all Shell sub-elements so
			// Shell's internal navigation system (GoToAsync) can resolve them
			EnsureShellItemHandlers();
		}

		// Set initial sidebar width
		_splitViewController?.SplitView?.SetPositionOfDivider(_flyoutWidth, 0);

		BuildSidebar();
	}

	protected override void DisconnectHandler(NSView platformView)
	{
		if (_shell != null)
		{
			((INotifyCollectionChanged)_shell.Items).CollectionChanged -= OnShellItemsChanged;
			_shell.Navigating -= OnShellNavigating;
			_shell.Navigated -= OnShellNavigated;
			_shell.PropertyChanged -= OnShellPropertyChanged;
		}
		if (_contentFrameChangedObserver != null)
		{
			NSNotificationCenter.DefaultCenter.RemoveObserver(_contentFrameChangedObserver);
			_contentFrameChangedObserver = null;
		}
		_shell = null;
		base.DisconnectHandler(platformView);
	}

	void OnShellItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		EnsureShellItemHandlers();
		BuildSidebar();
	}

	void EnsureShellItemHandlers()
	{
		if (_shell == null || MauiContext == null)
			return;

		foreach (var shellItem in _shell.Items)
		{
			if (shellItem is ShellItem item)
			{
				// Create handler for ShellItem if not already created
				item.Handler ??= ((IElement)item).ToHandler(MauiContext);

				foreach (var section in item.Items)
				{
					if (section is ShellSection shellSection)
					{
						// Create handler for ShellSection if not already created
						shellSection.Handler ??= ((IElement)shellSection).ToHandler(MauiContext);

						foreach (var content in shellSection.Items)
						{
							// Create handler for ShellContent if not already created
							content.Handler ??= ((IElement)content).ToHandler(MauiContext);
						}
					}
				}
			}
		}
	}

	void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
	{
	}

	void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		ShowCurrentPage();
	}

	void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(Shell.CurrentItem))
		{
			ShowCurrentPage();
		}
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);

		if (_contentView == null)
			return;

		if (!_useNativeSidebar)
			LayoutSidebarContent();

		if (_currentPageView != null)
		{
			var contentBounds = _contentView.Bounds;
			_currentPageView.Frame = contentBounds;
			LayoutCurrentPage(rect);
		}
	}

	void LayoutSidebarContent()
	{
		if (_sidebarContent == null || _sidebarView == null)
			return;

		var subviews = _sidebarContent.Subviews;
		if (subviews.Length == 0)
			return;

		var width = _sidebarView.Bounds.Width;
		nfloat y = 0;

		foreach (var subview in subviews)
		{
			var height = subview.IntrinsicContentSize.Height;
			if (height <= 0 || height > 10000)
				height = 36;
			subview.Frame = new CGRect(0, y, width, height);
			y += height;
		}

		_sidebarContent.Frame = new CGRect(0, 0, width, y);
	}

	void LayoutCurrentPage(Rect rect)
	{
		if (_currentPage == null || _contentView == null)
			return;

		var contentBounds = _contentView.Bounds;
		_currentPage.Measure((double)contentBounds.Width, (double)contentBounds.Height);
		_currentPage.Arrange(new Rect(0, 0, (double)contentBounds.Width, (double)contentBounds.Height));
	}

	void OnContentFrameChanged(NSNotification notification)
	{
		if (_contentView == null || _currentPageView == null || _currentPage == null)
			return;

		var bounds = _contentView.Bounds;
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return;

		_currentPageView.Frame = bounds;
		_currentPage.Measure((double)bounds.Width, (double)bounds.Height);
		_currentPage.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
	}

	void BuildSidebar()
	{
		if (_shell == null || MauiContext == null)
			return;

		if (_useNativeSidebar)
			BuildNativeSidebar();
		else
			BuildCustomSidebar();
	}

	void BuildNativeSidebar()
	{
		if (_outlineView == null || _shell == null)
			return;

		// Convert Shell.Items → MacOSSidebarItem hierarchy
		var sidebarItems = new List<MacOSSidebarItem>();
		_itemNavMap = new Dictionary<MacOSSidebarItem, (ShellItem, ShellSection, ShellContent)>();

		foreach (var shellItem in _shell.Items)
		{
			if (shellItem is not ShellItem item)
				continue;

			var sections = item.Items.ToList();
			var allContents = sections.SelectMany(s => s.Items).ToList();

			if (allContents.Count > 1 && !string.IsNullOrEmpty(item.Title))
			{
				// Group with children
				var group = new MacOSSidebarItem
				{
					Title = item.Title,
					Children = new List<MacOSSidebarItem>(),
				};

				foreach (var section in sections)
				{
					if (section is ShellSection shellSection)
					{
						foreach (var content in shellSection.Items)
						{
							var child = new MacOSSidebarItem
							{
								Title = content.Title ?? shellSection.Title ?? item.Title ?? "Page",
								SystemImage = GetSystemImageForContent(content, shellSection, item),
							};
							group.Children.Add(child);
							_itemNavMap[child] = (item, shellSection, content);
						}
					}
				}

				sidebarItems.Add(group);
			}
			else
			{
				// Single item (no group header)
				foreach (var section in sections)
				{
					if (section is ShellSection shellSection)
					{
						foreach (var content in shellSection.Items)
						{
							var leaf = new MacOSSidebarItem
							{
								Title = content.Title ?? shellSection.Title ?? item.Title ?? "Page",
								SystemImage = GetSystemImageForContent(content, shellSection, item),
							};
							sidebarItems.Add(leaf);
							_itemNavMap[leaf] = (item, shellSection, content);
						}
					}
				}
			}
		}

		_outlineDataSource = new SidebarOutlineViewDataSource(sidebarItems);
		_outlineDelegate = new SidebarOutlineViewDelegate(_outlineDataSource, OnNativeSidebarItemSelected);

		_outlineView.DataSource = _outlineDataSource;
		_outlineView.Delegate = _outlineDelegate;
		_outlineView.ReloadData();

		// Expand all groups and select current item
		foreach (var item in sidebarItems)
		{
			if (item.IsGroup)
			{
				var wrapper = _outlineDataSource.GetWrapper(item);
				_outlineView.ExpandItem(wrapper);
			}
		}

		SelectCurrentItemInOutlineView();
	}

	void SelectCurrentItemInOutlineView()
	{
		if (_outlineView == null || _shell == null || _itemNavMap == null || _outlineDataSource == null)
			return;

		_isUpdatingSelection = true;
		try
		{
			var currentItem = _shell.CurrentItem;
			var currentSection = currentItem?.CurrentItem;
			var currentContent = currentSection?.CurrentItem;

			foreach (var kvp in _itemNavMap)
			{
				if (kvp.Value.Item1 == currentItem &&
					kvp.Value.Item2 == currentSection &&
					kvp.Value.Item3 == currentContent)
				{
					var wrapper = _outlineDataSource.GetWrapper(kvp.Key);
					var row = _outlineView.RowForItem(wrapper);
					if (row >= 0)
					{
						_outlineView.SelectRow(row, false);
					}
					break;
				}
			}
		}
		finally
		{
			_isUpdatingSelection = false;
		}
	}

	void OnNativeSidebarItemSelected(MacOSSidebarItem sidebarItem)
	{
		if (_shell == null || _itemNavMap == null || _isUpdatingSelection)
			return;

		if (_itemNavMap.TryGetValue(sidebarItem, out var nav))
		{
			_shell.CurrentItem = nav.Item1;
			nav.Item1.CurrentItem = nav.Item2;
			nav.Item2.CurrentItem = nav.Item3;

			// ShowCurrentPage must be called explicitly because Shell.CurrentItem
			// PropertyChanged won't fire when navigating within the same FlyoutItem
			ShowCurrentPage();
		}
	}

	/// <summary>
	/// Gets the SF Symbol name for a Shell content item, checking attached properties
	/// on content → section → item in priority order.
	/// </summary>
	static string? GetSystemImageForContent(ShellContent content, ShellSection section, ShellItem item)
	{
		// Check MacOSShell.SystemImage attached property (content → section → item)
		var image = MacOSShell.GetSystemImage(content);
		if (!string.IsNullOrEmpty(image)) return image;

		image = MacOSShell.GetSystemImage(section);
		if (!string.IsNullOrEmpty(image)) return image;

		image = MacOSShell.GetSystemImage(item);
		if (!string.IsNullOrEmpty(image)) return image;

		return null;
	}

	void BuildCustomSidebar()
	{
		if (_sidebarContent == null || _shell == null || MauiContext == null)
			return;

		// Clear existing sidebar items
		foreach (var subview in _sidebarContent.Subviews)
			subview.RemoveFromSuperview();

		// Add items from Shell.Items (ShellItem collection)
		foreach (var shellItem in _shell.Items)
		{
			if (shellItem is not ShellItem item)
				continue;

			var sections = item.Items.ToList();
			var allContents = sections.SelectMany(s => s.Items).ToList();
			var hasMultipleContents = allContents.Count > 1;

			// Show group header if FlyoutItem has multiple children
			if (hasMultipleContents && !string.IsNullOrEmpty(item.Title))
			{
				_sidebarContent.AddSubview(new SidebarGroupHeaderView(item.Title));
			}

			foreach (var section in sections)
			{
				if (section is ShellSection shellSection)
				{
					foreach (var content in shellSection.Items)
					{
						AddSidebarItem(content.Title ?? shellSection.Title ?? item.Title ?? "Page",
							content.Icon ?? shellSection.Icon ?? item.Icon,
							shellItem, shellSection, content, hasMultipleContents);
					}
				}
			}
		}

		LayoutSidebarContent();
	}

	void AddSidebarItem(string title, ImageSource? icon, ShellItem shellItem, ShellSection section, ShellContent content, bool indented)
	{
		var itemView = new SidebarItemView(title, indented, () =>
		{
			if (_shell == null)
				return;

			// Navigate to the selected item
			_shell.CurrentItem = shellItem;
			shellItem.CurrentItem = section;
			section.CurrentItem = content;
		});

		// Highlight if this is the current item
		if (_shell?.CurrentItem == shellItem &&
			shellItem.CurrentItem == section &&
			section.CurrentItem == content)
		{
			itemView.SetSelected(true);
		}

		_sidebarContent!.AddSubview(itemView);
	}

	internal void ShowCurrentPage()
	{
		// Must run on main thread — AppKit view creation/manipulation
		// crashes with SIGSEGV when called from background threads
		// (e.g., when Shell.GoToAsync is called from a non-UI thread).
		if (!NSThread.IsMain)
		{
			NSApplication.SharedApplication.InvokeOnMainThread(ShowCurrentPage);
			return;
		}

		if (_contentView == null || _shell == null || MauiContext == null)
			return;

		// Remove old page
		if (_currentPageView != null)
		{
			_currentPageView.RemoveFromSuperview();
			_currentPageView = null;
			_currentPage = null;
		}

		Page? page = null;

		// Check if there are pushed pages on the ShellSection navigation stack
		var currentItem = _shell.CurrentItem;
		if (currentItem?.CurrentItem is ShellSection section)
		{
			var navStack = section.Navigation?.NavigationStack;
			if (navStack != null && navStack.Count > 1)
			{
				// Show the topmost pushed page
				page = navStack[^1];
			}
		}

		// Fall back to root content from ShellContent
		if (page == null &&
			currentItem?.CurrentItem?.CurrentItem is ShellContent shellContent &&
			shellContent is IShellContentController controller)
		{
			page = controller.GetOrCreateContent();
		}

		if (page != null)
		{
			_currentPage = page;
			try
			{
				var platformView = ((IView)page).ToMacOSPlatform(MauiContext);
				platformView.Frame = _contentView.Bounds;
				platformView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
				_contentView.AddSubview(platformView);
				_currentPageView = platformView;

				// Measure and arrange
				var bounds = _contentView.Bounds;
				if (bounds.Width > 0 && bounds.Height > 0)
				{
					page.Measure((double)bounds.Width, (double)bounds.Height);
					page.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[ShellHandler.ShowCurrentPage] Failed to create page view: {ex.Message}");
			}
		}

		// Update sidebar selection
		if (_useNativeSidebar)
			SelectCurrentItemInOutlineView();
		else
			BuildCustomSidebar();

		// Notify WindowHandler to refresh toolbar (back button, title, toolbar items)
		NotifyToolbarRefresh();
	}

	void NotifyToolbarRefresh()
	{
		if (_shell?.Window?.Handler is WindowHandler windowHandler)
		{
			windowHandler.RefreshToolbar();
		}
	}

	// Property mappers

	public static void MapCurrentItem(ShellHandler handler, Shell shell)
	{
		handler.ShowCurrentPage();
	}

	public static void MapFlyoutBackground(ShellHandler handler, Shell shell)
	{
		if (handler._sidebarView != null && shell.FlyoutBackgroundColor != null)
		{
			handler._sidebarView.WantsLayer = true;
			handler._sidebarView.Layer!.BackgroundColor = shell.FlyoutBackgroundColor.ToPlatformColor().CGColor;
		}
	}

	public static void MapFlyoutHeader(ShellHandler handler, Shell shell)
	{
		// Header support could be added via FlyoutHeaderTemplate
	}

	public static void MapItems(ShellHandler handler, Shell shell)
	{
		handler.BuildSidebar();
		handler.UpdateValue(nameof(Shell.CurrentItem));
	}

	public static void MapFlyoutItems(ShellHandler handler, Shell shell)
	{
		handler.BuildSidebar();
	}

	public static void MapFlyoutBehavior(ShellHandler handler, Shell shell)
	{
		if (handler._sidebarSplitItem == null)
			return;

		handler._sidebarSplitItem.Collapsed = shell.FlyoutBehavior == FlyoutBehavior.Disabled;
	}

	public static void MapIsPresented(ShellHandler handler, Shell shell)
	{
		if (handler._sidebarSplitItem == null)
			return;

		// When FlyoutBehavior is Locked, sidebar is always visible
		if (shell.FlyoutBehavior == FlyoutBehavior.Locked)
			return;

		handler._sidebarSplitItem.Collapsed = !shell.FlyoutIsPresented;
	}

	public static void MapFlyoutWidth(ShellHandler handler, Shell shell)
	{
		if (shell.FlyoutWidth > 0)
		{
			handler._flyoutWidth = (nfloat)shell.FlyoutWidth;
			handler._splitViewController?.SplitView?.SetPositionOfDivider(handler._flyoutWidth, 0);
		}
	}

	/// <summary>
	/// A non-interactive group header label for the sidebar.
	/// </summary>
	class SidebarGroupHeaderView : NSView
	{
		readonly NSTextField _label;

		public SidebarGroupHeaderView(string title)
		{
			_label = new NSTextField
			{
				StringValue = title.ToUpperInvariant(),
				Editable = false,
				Bordered = false,
				DrawsBackground = false,
				Font = NSFont.BoldSystemFontOfSize(10),
				TextColor = NSColor.SecondaryLabel,
				LineBreakMode = NSLineBreakMode.TruncatingTail,
			};
			AddSubview(_label);
		}

		public override CGSize IntrinsicContentSize => new CGSize(NSView.NoIntrinsicMetric, 28);

		public override void Layout()
		{
			base.Layout();
			_label.Frame = new CGRect(16, 10, Bounds.Width - 32, 14);
		}
	}

	/// <summary>
	/// A simple sidebar item view using native NSTextField and NSView.
	/// </summary>
	class SidebarItemView : NSView
	{
		readonly NSTextField _label;
		readonly Action _onTap;
		readonly bool _indented;
		bool _isSelected;
		NSTrackingArea? _trackingArea;

		public SidebarItemView(string title, bool indented, Action onTap)
		{
			_onTap = onTap;
			_indented = indented;
			WantsLayer = true;
			Layer!.CornerRadius = 6;

			_label = new NSTextField
			{
				StringValue = title,
				Editable = false,
				Bordered = false,
				DrawsBackground = false,
				Font = NSFont.SystemFontOfSize(13),
				TextColor = NSColor.Label,
				LineBreakMode = NSLineBreakMode.TruncatingTail,
			};

			AddSubview(_label);
		}

		public override CGSize IntrinsicContentSize => new CGSize(NSView.NoIntrinsicMetric, 30);

		public override void Layout()
		{
			base.Layout();
			var leftPad = _indented ? (nfloat)28 : (nfloat)16;
			_label.Frame = new CGRect(leftPad, 5, Bounds.Width - leftPad - 12, 20);
		}

		public void SetSelected(bool selected)
		{
			_isSelected = selected;
			Layer!.BackgroundColor = selected
				? NSColor.SelectedContentBackground.CGColor
				: NSColor.Clear.CGColor;
			_label.TextColor = selected ? NSColor.White : NSColor.Label;
		}

		public override void MouseDown(NSEvent theEvent)
		{
			base.MouseDown(theEvent);
			_onTap();
		}

		public override void UpdateTrackingAreas()
		{
			base.UpdateTrackingAreas();

			if (_trackingArea != null)
				RemoveTrackingArea(_trackingArea);

			_trackingArea = new NSTrackingArea(
				Bounds,
				NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveInKeyWindow,
				this,
				null);
			AddTrackingArea(_trackingArea);
		}

		public override void MouseEntered(NSEvent theEvent)
		{
			base.MouseEntered(theEvent);
			if (!_isSelected)
				Layer!.BackgroundColor = NSColor.UnemphasizedSelectedContentBackground.CGColor;
		}

		public override void MouseExited(NSEvent theEvent)
		{
			base.MouseExited(theEvent);
			if (!_isSelected)
				Layer!.BackgroundColor = NSColor.Clear.CGColor;
		}
	}

}
