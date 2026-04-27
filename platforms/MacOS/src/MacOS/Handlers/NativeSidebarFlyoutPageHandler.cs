using System.Collections.Specialized;
using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// NSObject wrapper for MacOSSidebarItem, required for NSOutlineView identity tracking.
/// </summary>
internal class SidebarItemWrapper : NSObject
{
	public MacOSSidebarItem Item { get; }
	public SidebarItemWrapper(MacOSSidebarItem item) => Item = item;
}

/// <summary>
/// NSOutlineView data source for native sidebar items.
/// </summary>
internal class SidebarOutlineViewDataSource : NSOutlineViewDataSource
{
	readonly IList<MacOSSidebarItem> _items;
	readonly Dictionary<MacOSSidebarItem, SidebarItemWrapper> _wrappers = new();

	public SidebarOutlineViewDataSource(IList<MacOSSidebarItem> items)
	{
		_items = items;
		BuildWrappers(items);
	}

	void BuildWrappers(IList<MacOSSidebarItem> items)
	{
		foreach (var item in items)
		{
			_wrappers[item] = new SidebarItemWrapper(item);
			if (item.Children != null)
				BuildWrappers(item.Children);
		}
	}

	public SidebarItemWrapper GetWrapper(MacOSSidebarItem item)
	{
		if (!_wrappers.TryGetValue(item, out var wrapper))
		{
			wrapper = new SidebarItemWrapper(item);
			_wrappers[item] = wrapper;
		}
		return wrapper;
	}

	public MacOSSidebarItem? GetItem(NSObject? obj)
		=> (obj as SidebarItemWrapper)?.Item;

	public override nint GetChildrenCount(NSOutlineView outlineView, NSObject? item)
	{
		if (item == null)
			return _items.Count;

		var sidebarItem = GetItem(item);
		return sidebarItem?.Children?.Count ?? 0;
	}

	public override NSObject GetChild(NSOutlineView outlineView, nint childIndex, NSObject? item)
	{
		MacOSSidebarItem child;
		if (item == null)
			child = _items[(int)childIndex];
		else
		{
			var parent = GetItem(item)!;
			child = parent.Children![(int)childIndex];
		}
		return GetWrapper(child);
	}

	public override bool ItemExpandable(NSOutlineView outlineView, NSObject item)
	{
		var sidebarItem = GetItem(item);
		return sidebarItem?.IsGroup ?? false;
	}
}

/// <summary>
/// NSOutlineView delegate for native sidebar rendering and selection.
/// </summary>
internal class SidebarOutlineViewDelegate : NSOutlineViewDelegate
{
	readonly SidebarOutlineViewDataSource _dataSource;
	readonly Action<MacOSSidebarItem> _onSelectionChanged;

	public SidebarOutlineViewDelegate(SidebarOutlineViewDataSource dataSource, Action<MacOSSidebarItem> onSelectionChanged)
	{
		_dataSource = dataSource;
		_onSelectionChanged = onSelectionChanged;
	}

	public override bool IsGroupItem(NSOutlineView outlineView, NSObject item)
	{
		var sidebarItem = _dataSource.GetItem(item);
		return sidebarItem?.IsGroup ?? false;
	}

	public override NSView GetView(NSOutlineView outlineView, NSTableColumn? tableColumn, NSObject item)
	{
		var sidebarItem = _dataSource.GetItem(item);
		if (sidebarItem == null)
			return new NSView();

		if (sidebarItem.IsGroup)
		{
			// Section header — plain text, uppercase-ish system style
			var headerView = outlineView.MakeView("HeaderCell", this) as NSTableCellView
				?? CreateHeaderCellView();
			headerView.TextField!.StringValue = sidebarItem.Title;
			return headerView;
		}

		// Regular item — icon + text
		var cellView = outlineView.MakeView("DataCell", this) as NSTableCellView
			?? CreateDataCellView();
		cellView.TextField!.StringValue = sidebarItem.Title;

		if (cellView.ImageView != null)
		{
			NSImage? image = null;
			if (!string.IsNullOrEmpty(sidebarItem.SystemImage))
			{
				image = NSImage.GetSystemSymbol(sidebarItem.SystemImage, null);
			}
			cellView.ImageView.Image = image;
			cellView.ImageView.Hidden = image == null;
		}

		return cellView;
	}

	NSTableCellView CreateHeaderCellView()
	{
		var cell = new NSTableCellView { Identifier = "HeaderCell" };
		var textField = new NSTextField
		{
			Editable = false,
			Bordered = false,
			DrawsBackground = false,
			Font = NSFont.BoldSystemFontOfSize(11),
			TextColor = NSColor.SecondaryLabel,
			TranslatesAutoresizingMaskIntoConstraints = false,
		};
		cell.AddSubview(textField);
		cell.TextField = textField;

		NSLayoutConstraint.ActivateConstraints(new[]
		{
			textField.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 4),
			textField.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, -4),
			textField.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
		});

		return cell;
	}

	NSTableCellView CreateDataCellView()
	{
		var cell = new NSTableCellView { Identifier = "DataCell" };

		var imageView = new NSImageView
		{
			TranslatesAutoresizingMaskIntoConstraints = false,
			ImageScaling = NSImageScale.ProportionallyUpOrDown,
		};
		imageView.SetContentHuggingPriorityForOrientation(251, NSLayoutConstraintOrientation.Horizontal);

		var textField = new NSTextField
		{
			Editable = false,
			Bordered = false,
			DrawsBackground = false,
			Font = NSFont.SystemFontOfSize(13),
			TextColor = NSColor.Label,
			LineBreakMode = NSLineBreakMode.TruncatingTail,
			TranslatesAutoresizingMaskIntoConstraints = false,
		};

		cell.AddSubview(imageView);
		cell.AddSubview(textField);
		cell.ImageView = imageView;
		cell.TextField = textField;

		NSLayoutConstraint.ActivateConstraints(new[]
		{
			imageView.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 4),
			imageView.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
			imageView.WidthAnchor.ConstraintEqualTo(18),
			imageView.HeightAnchor.ConstraintEqualTo(18),

			textField.LeadingAnchor.ConstraintEqualTo(imageView.TrailingAnchor, 6),
			textField.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, -4),
			textField.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
		});

		return cell;
	}

	public override void SelectionDidChange(NSNotification notification)
	{
		if (notification.Object is not NSOutlineView outlineView)
			return;

		var selectedRow = outlineView.SelectedRow;
		if (selectedRow < 0)
			return;

		var item = outlineView.ItemAtRow(selectedRow);
		var sidebarItem = _dataSource.GetItem(item as NSObject);
		if (sidebarItem != null && !sidebarItem.IsGroup)
		{
			_onSelectionChanged(sidebarItem);
		}
	}

	public override nfloat GetRowHeight(NSOutlineView outlineView, NSObject item)
	{
		var sidebarItem = _dataSource.GetItem(item);
		return sidebarItem?.IsGroup == true ? 24 : 28;
	}

	public override bool ShouldSelectItem(NSOutlineView outlineView, NSObject item)
	{
		var sidebarItem = _dataSource.GetItem(item);
		return sidebarItem != null && !sidebarItem.IsGroup;
	}
}

/// <summary>
/// Alternative FlyoutPage handler that uses a native NSOutlineView source list for the sidebar.
/// Opt in by registering this handler and providing items via <see cref="MacOSFlyoutPage.SidebarItemsProperty"/>.
/// 
/// <code>
/// // In MauiProgram.cs:
/// builder.ConfigureMauiHandlers(handlers =>
///     handlers.AddHandler&lt;FlyoutPage, NativeSidebarFlyoutPageHandler&gt;());
///
/// // In page setup:
/// MacOSFlyoutPage.SetSidebarItems(flyoutPage, items);
/// MacOSFlyoutPage.SetSidebarSelectionChanged(flyoutPage, item => { ... });
/// </code>
/// </summary>
public partial class NativeSidebarFlyoutPageHandler : MacOSViewHandler<IFlyoutView, NSSplitView>
{
	public static readonly IPropertyMapper<IFlyoutView, NativeSidebarFlyoutPageHandler> Mapper =
		new PropertyMapper<IFlyoutView, NativeSidebarFlyoutPageHandler>(ViewMapper)
		{
			[nameof(IFlyoutView.Detail)] = MapDetail,
			[nameof(IFlyoutView.IsPresented)] = MapIsPresented,
			[nameof(IFlyoutView.FlyoutBehavior)] = MapFlyoutBehavior,
			[nameof(IFlyoutView.FlyoutWidth)] = MapFlyoutWidth,
		};

	NSSplitViewController? _splitViewController;
	NSSplitViewItem? _sidebarSplitItem;

	NSScrollView? _scrollView;
	NSOutlineView? _outlineView;
	NSVisualEffectView? _sidebarEffectView;
	NSView? _detailContainer;
	NSView? _currentDetailView;
	NSLayoutConstraint? _sidebarWidthConstraint;

	SidebarOutlineViewDataSource? _dataSource;
	SidebarOutlineViewDelegate? _delegate;

	double _flyoutWidth = 250;

	/// <summary>
	/// True when the handler is updating the selection programmatically.
	/// Used to prevent re-entrant calls between the attached property and the handler.
	/// </summary>
	internal bool IsProgrammaticSelection { get; private set; }

	/// <summary>
	/// Exposes the NSSplitViewController so WindowHandler can set it as the
	/// window's contentViewController for proper sidebar titlebar integration.
	/// </summary>
	public NSSplitViewController? SplitViewController => _splitViewController;

	FlyoutPage? FlyoutPage => VirtualView as FlyoutPage;

	public NativeSidebarFlyoutPageHandler() : base(Mapper) { }

	protected override NSSplitView CreatePlatformView()
	{
		bool useNativeSidebar = VirtualView is Microsoft.Maui.Controls.FlyoutPage fp
			&& MacOSFlyoutPage.GetUseNativeSidebar(fp);

		// NSVisualEffectView with sidebar material for translucent sidebar appearance.
		// On macOS 26+ linked with SDK 26, AppKit automatically applies the
		// Liquid Glass style to views using the sidebar material.
		_sidebarEffectView = new NSVisualEffectView
		{
			Material = NSVisualEffectMaterial.Sidebar,
			State = NSVisualEffectState.Active,
			BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
			TranslatesAutoresizingMaskIntoConstraints = false,
		};

		_outlineView = new NSOutlineView
		{
			Style = NSTableViewStyle.SourceList,
			FloatsGroupRows = false,
			IndentationPerLevel = 0,
			HeaderView = null,
		};

		var column = new NSTableColumn("SidebarColumn") { Editable = false };
		_outlineView.AddColumn(column);
		_outlineView.OutlineTableColumn = column;

		_scrollView = new NSScrollView
		{
			HasVerticalScroller = true,
			HasHorizontalScroller = false,
			AutohidesScrollers = true,
			DrawsBackground = false,
			DocumentView = _outlineView,
			TranslatesAutoresizingMaskIntoConstraints = false,
		};

		_sidebarEffectView.AddSubview(_scrollView);

		NSLayoutConstraint.ActivateConstraints(new[]
		{
			_scrollView.TopAnchor.ConstraintEqualTo(_sidebarEffectView.TopAnchor),
			_scrollView.LeadingAnchor.ConstraintEqualTo(_sidebarEffectView.LeadingAnchor),
			_scrollView.TrailingAnchor.ConstraintEqualTo(_sidebarEffectView.TrailingAnchor),
			_scrollView.BottomAnchor.ConstraintEqualTo(_sidebarEffectView.BottomAnchor),
		});

		_detailContainer = new NSView { WantsLayer = true };

		if (useNativeSidebar)
		{
			// Use NSSplitViewController for inset sidebar with vibrancy and titlebar integration
			_splitViewController = new NSSplitViewController();

			var sidebarVC = new NSViewController { View = _sidebarEffectView };
			var contentVC = new NSViewController { View = _detailContainer };

			_sidebarSplitItem = NSSplitViewItem.CreateSidebar(sidebarVC);
			_sidebarSplitItem.MinimumThickness = 150;
			_sidebarSplitItem.MaximumThickness = 400;
			_sidebarSplitItem.CanCollapse = false;
			_sidebarSplitItem.AllowsFullHeightLayout = true;
			_sidebarSplitItem.TitlebarSeparatorStyle = NSTitlebarSeparatorStyle.None;

			var contentItem = NSSplitViewItem.CreateContentList(contentVC);
			contentItem.TitlebarSeparatorStyle = NSTitlebarSeparatorStyle.Line;

			_splitViewController.AddSplitViewItem(_sidebarSplitItem);
			_splitViewController.AddSplitViewItem(contentItem);

			// Observe detail frame changes for MAUI layout
			_detailContainer.PostsFrameChangedNotifications = true;
			Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
				NSView.FrameChangedNotification, OnDetailFrameChanged, _detailContainer);

			return _splitViewController.SplitView;
		}

		var splitView = new NSSplitView
		{
			IsVertical = true,
			DividerStyle = NSSplitViewDividerStyle.Thin,
		};

		splitView.AddArrangedSubview(_sidebarEffectView);
		splitView.AddArrangedSubview(_detailContainer);

		_sidebarWidthConstraint = _sidebarEffectView.WidthAnchor.ConstraintEqualTo((nfloat)_flyoutWidth);
		_sidebarWidthConstraint.Priority = (float)NSLayoutPriority.DefaultHigh;
		_sidebarWidthConstraint.Active = true;

		splitView.SetHoldingPriority(251, 0);
		splitView.SetHoldingPriority(249, 1);

		return splitView;
	}

	protected override void ConnectHandler(NSSplitView platformView)
	{
		base.ConnectHandler(platformView);
		LoadSidebarItems();

		// Defer initial sidebar width to next run loop so the view is in a window
		if (_splitViewController != null)
		{
			var width = _flyoutWidth;
			CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
			{
				_splitViewController?.SplitView?.SetPositionOfDivider((nfloat)width, 0);
			});
		}
	}

	protected override void DisconnectHandler(NSSplitView platformView)
	{
		_dataSource = null;
		_delegate = null;
		if (_detailContainer != null)
			Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(
				_detailContainer, NSView.FrameChangedNotification, null);
		base.DisconnectHandler(platformView);
	}

	void OnDetailFrameChanged(Foundation.NSNotification notification)
	{
		if (_currentDetailView == null || _detailContainer == null)
			return;

		var bounds = _detailContainer.Bounds;
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return;

		_currentDetailView.Frame = bounds;

		var detail = VirtualView?.Detail;
		if (detail != null)
		{
			detail.Measure((double)bounds.Width, (double)bounds.Height);
			detail.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
		}
	}

	void LoadSidebarItems()
	{
		if (_outlineView == null || FlyoutPage == null)
			return;

		var items = MacOSFlyoutPage.GetSidebarItems(FlyoutPage);
		if (items == null || items.Count == 0)
			return;

		_dataSource = new SidebarOutlineViewDataSource(items);
		_delegate = new SidebarOutlineViewDelegate(_dataSource, OnSidebarItemSelected);

		_outlineView.DataSource = _dataSource;
		_outlineView.Delegate = _delegate;
		_outlineView.ReloadData();

		// Expand all group items
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].IsGroup)
			{
				var wrapper = _dataSource.GetWrapper(items[i]);
				_outlineView.ExpandItem(wrapper);
			}
		}

		// Select the first selectable item
		SelectFirstItem(items);

		// Watch for collection changes
		if (items is INotifyCollectionChanged observable)
		{
			observable.CollectionChanged += (s, e) => ReloadSidebar();
		}
	}

	void SelectFirstItem(IList<MacOSSidebarItem> items)
	{
		foreach (var item in items)
		{
			if (item.IsGroup && item.Children != null)
			{
				foreach (var child in item.Children)
				{
					var wrapper = _dataSource!.GetWrapper(child);
					var row = _outlineView!.RowForItem(wrapper);
					if (row >= 0)
					{
						_outlineView.SelectRow(row, false);
						return;
					}
				}
			}
			else if (!item.IsGroup)
			{
				var wrapper = _dataSource!.GetWrapper(item);
				var row = _outlineView!.RowForItem(wrapper);
				if (row >= 0)
				{
					_outlineView.SelectRow(row, false);
					return;
				}
			}
		}
	}

	/// <summary>
	/// Programmatically selects a sidebar item in the NSOutlineView.
	/// </summary>
	/// <param name="item">The sidebar item to select.</param>
	/// <param name="suppressCallback">When true, the SidebarSelectionChanged callback is not fired.</param>
	public void SelectItem(MacOSSidebarItem item, bool suppressCallback = false)
	{
		if (_outlineView == null || _dataSource == null)
			return;

		var wrapper = _dataSource.GetWrapper(item);
		var row = _outlineView.RowForItem(wrapper);
		if (row < 0)
			return;

		if (suppressCallback)
			IsProgrammaticSelection = true;

		try
		{
			_outlineView.SelectRow(row, false);
		}
		finally
		{
			IsProgrammaticSelection = false;
		}
	}

	/// <summary>
	/// Selects the first sidebar item matching the given predicate.
	/// </summary>
	/// <param name="predicate">A function to test each item.</param>
	/// <param name="suppressCallback">When true, the SidebarSelectionChanged callback is not fired.</param>
	/// <returns>The matched item, or null if not found.</returns>
	public MacOSSidebarItem? SelectItem(Func<MacOSSidebarItem, bool> predicate, bool suppressCallback = false)
	{
		if (FlyoutPage == null)
			return null;

		var items = MacOSFlyoutPage.GetSidebarItems(FlyoutPage);
		if (items == null)
			return null;

		var match = FindItem(items, predicate);
		if (match != null)
			SelectItem(match, suppressCallback);

		return match;
	}

	static MacOSSidebarItem? FindItem(IList<MacOSSidebarItem> items, Func<MacOSSidebarItem, bool> predicate)
	{
		foreach (var item in items)
		{
			if (!item.IsGroup && predicate(item))
				return item;

			if (item.Children != null)
			{
				var found = FindItem(item.Children, predicate);
				if (found != null)
					return found;
			}
		}
		return null;
	}

	void ReloadSidebar()
	{
		if (_outlineView == null || FlyoutPage == null)
			return;

		var items = MacOSFlyoutPage.GetSidebarItems(FlyoutPage);
		if (items == null)
			return;

		_dataSource = new SidebarOutlineViewDataSource(items);
		_delegate = new SidebarOutlineViewDelegate(_dataSource, OnSidebarItemSelected);
		_outlineView.DataSource = _dataSource;
		_outlineView.Delegate = _delegate;
		_outlineView.ReloadData();

		// Expand all groups
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].IsGroup)
			{
				var wrapper = _dataSource.GetWrapper(items[i]);
				_outlineView.ExpandItem(wrapper);
			}
		}
	}

	void OnSidebarItemSelected(MacOSSidebarItem item)
	{
		if (FlyoutPage == null)
			return;

		// When the handler is doing a programmatic selection (suppressCallback),
		// only update the attached property, skip the user callback.
		var wasProgrammatic = IsProgrammaticSelection;

		// Update the SelectedItem attached property (without re-triggering SelectItem)
		IsProgrammaticSelection = true;
		try
		{
			MacOSFlyoutPage.SetSelectedItem(FlyoutPage, item);
		}
		finally
		{
			IsProgrammaticSelection = wasProgrammatic;
		}

		if (!wasProgrammatic)
		{
			var callback = MacOSFlyoutPage.GetSidebarSelectionChanged(FlyoutPage);
			callback?.Invoke(item);
		}
	}

	void ShowDetail(NSView view)
	{
		if (_detailContainer == null)
			return;

		_currentDetailView?.RemoveFromSuperview();
		_currentDetailView = view;

		view.Frame = _detailContainer.Bounds;
		view.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
		_detailContainer.AddSubview(view);
	}

	void LayoutDetail()
	{
		if (_detailContainer == null || _currentDetailView == null)
			return;

		_currentDetailView.Frame = _detailContainer.Bounds;

		var detail = VirtualView?.Detail;
		if (detail != null)
		{
			var bounds = _detailContainer.Bounds;
			if (bounds.Width > 0 && bounds.Height > 0)
			{
				detail.Measure((double)bounds.Width, (double)bounds.Height);
				detail.Arrange(new Rect(0, 0, (double)bounds.Width, (double)bounds.Height));
			}
		}
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		LayoutDetail();
	}

	// Property mappers

	public static void MapDetail(NativeSidebarFlyoutPageHandler handler, IFlyoutView view)
	{
		if (handler.MauiContext == null || view.Detail == null)
			return;

		var platformView = view.Detail.ToMacOSPlatform(handler.MauiContext);
		handler.ShowDetail(platformView);
	}

	public static void MapIsPresented(NativeSidebarFlyoutPageHandler handler, IFlyoutView view)
	{
		if (handler._sidebarEffectView == null)
			return;

		handler._sidebarEffectView.Hidden = !view.IsPresented;
	}

	public static void MapFlyoutBehavior(NativeSidebarFlyoutPageHandler handler, IFlyoutView view)
	{
		if (handler._sidebarEffectView == null)
			return;

		switch (view.FlyoutBehavior)
		{
			case FlyoutBehavior.Disabled:
				handler._sidebarEffectView.Hidden = true;
				break;
			case FlyoutBehavior.Locked:
				handler._sidebarEffectView.Hidden = false;
				break;
			case FlyoutBehavior.Flyout:
				handler._sidebarEffectView.Hidden = !view.IsPresented;
				break;
		}
	}

	public static void MapFlyoutWidth(NativeSidebarFlyoutPageHandler handler, IFlyoutView view)
	{
		var width = view.FlyoutWidth > 0 ? view.FlyoutWidth : 250;
		handler._flyoutWidth = width;
		if (handler._sidebarWidthConstraint != null)
			handler._sidebarWidthConstraint.Constant = (nfloat)width;
		handler._splitViewController?.SplitView?.SetPositionOfDivider((nfloat)width, 0);
	}
}
