using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Comet.MacOS;

namespace Comet.Handlers
{
	public partial class ListViewHandler : ViewHandler<IListView, NSScrollView>
	{
		NSTableView _tableView;
		ListViewDataSource _dataSource;
		ListViewDelegate _delegate;

		public static void MapListViewProperty(IElementHandler viewHandler, IListView virtualView)
		{
			var handler = (ListViewHandler)viewHandler;
			handler.UpdateListView(virtualView);
		}

#nullable enable
		public static void MapReloadData(ListViewHandler viewHandler, IListView virtualView, object? value)
#nullable restore
		{
			viewHandler.UpdateListView(virtualView);
		}

		void UpdateListView(IListView listView)
		{
			if (_dataSource is null || _delegate is null || _tableView is null)
				return;

			_dataSource.ListView = listView;
			_delegate.ListView = listView;
			_tableView.ReloadData();
		}

		protected override NSScrollView CreatePlatformView()
		{
			_tableView = new NSTableView();

			var column = new NSTableColumn("Content") { Title = "" };
			_tableView.AddColumn(column);
			_tableView.HeaderView = null;
			_tableView.UsesAlternatingRowBackgroundColors = false;
			_tableView.SelectionHighlightStyle = NSTableViewSelectionHighlightStyle.Regular;
			_tableView.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.LastColumnOnly;

			_dataSource = new ListViewDataSource();
			_delegate = new ListViewDelegate(MauiContext, _dataSource);

			_tableView.DataSource = _dataSource;
			_tableView.Delegate = _delegate;
			_tableView.SizeLastColumnToFit();

			var scrollView = new NSScrollView
			{
				DocumentView = _tableView,
				HasVerticalScroller = true,
				AutohidesScrollers = true,
			};
			return scrollView;
		}

		protected override void DisconnectHandler(NSScrollView platformView)
		{
			if (_tableView is not null)
			{
				_tableView.DataSource = null;
				_tableView.Delegate = null;
			}
			_dataSource = null;
			_delegate = null;
			_tableView = null;
			base.DisconnectHandler(platformView);
		}

		public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
			return new Microsoft.Maui.Graphics.Size(
				double.IsInfinity(widthConstraint) ? 400 : widthConstraint,
				double.IsInfinity(heightConstraint) ? 600 : heightConstraint);
		}

		// NSTableView is flat — no native sections. We flatten (section, row) pairs
		// into a single row index, interleaving section headers when present.
		internal enum RowKind
		{
			Header,
			Item,
		}

		internal struct FlatRow
		{
			public RowKind Kind;
			public int Section;
			public int Index;
		}

		static List<FlatRow> BuildFlatRows(IListView listView)
		{
			var rows = new List<FlatRow>();
			if (listView is null)
				return rows;

			var sectionCount = listView.Sections();
			for (int s = 0; s < sectionCount; s++)
			{
				var header = listView.HeaderFor(s);
				if (header is not null)
				{
					rows.Add(new FlatRow { Kind = RowKind.Header, Section = s, Index = -1 });
				}

				var rowCount = listView.Rows(s);
				for (int r = 0; r < rowCount; r++)
				{
					rows.Add(new FlatRow { Kind = RowKind.Item, Section = s, Index = r });
				}
			}

			return rows;
		}

		class ListViewDataSource : NSTableViewDataSource
		{
			List<FlatRow> _flatRows = new List<FlatRow>();
			IListView _listView;

			public IListView ListView
			{
				get => _listView;
				set
				{
					_listView = value;
					_flatRows = BuildFlatRows(_listView);
				}
			}

			public List<FlatRow> FlatRows => _flatRows;

			public override nint GetRowCount(NSTableView tableView)
			{
				return _flatRows.Count;
			}
		}

		class ListViewDelegate : NSTableViewDelegate
		{
			const string CellIdentifier = "CometListCell";
			const string HeaderIdentifier = "CometListHeader";
			static readonly nfloat DefaultRowHeight = 44;

			readonly IMauiContext _mauiContext;
			readonly ListViewDataSource _dataSource;
			IListView _listView;

			public ListViewDelegate(IMauiContext mauiContext, ListViewDataSource dataSource)
			{
				_mauiContext = mauiContext;
				_dataSource = dataSource;
			}

			public IListView ListView
			{
				get => _listView;
				set => _listView = value;
			}

			[Export("tableView:viewForTableColumn:row:")]
			public NSView GetViewForTableColumn(NSTableView tableView, NSTableColumn tableColumn, nint row)
			{
				if (_listView is null || row < 0 || row >= _dataSource.FlatRows.Count)
					return new NSView();

				var flatRow = _dataSource.FlatRows[(int)row];

				if (flatRow.Kind == RowKind.Header)
				{
					var headerView = _listView.HeaderFor(flatRow.Section);
					if (headerView is null)
						return new NSView();

					var container = tableView.MakeView(HeaderIdentifier, this) as CometNSTableCellView;
					if (container is null)
					{
						container = new CometNSTableCellView(_mauiContext)
						{
							Identifier = HeaderIdentifier,
						};
					}
					container.SetView(headerView);
					return container;
				}

				var view = _listView.ViewFor(flatRow.Section, flatRow.Index);
				if (view is null)
					return new NSView();

				// Use content type hash for view recycling (same pattern as iOS)
				var identifier = $"{CellIdentifier}_{view.GetContentTypeHashCode()}";
				var cellView = tableView.MakeView(identifier, this) as CometNSTableCellView;
				if (cellView is null)
				{
					cellView = new CometNSTableCellView(_mauiContext)
					{
						Identifier = identifier,
					};
				}
				cellView.SetView(view);
				return cellView;
			}

			public override nfloat GetRowHeight(NSTableView tableView, nint row)
			{
				if (_listView is null || row < 0 || row >= _dataSource.FlatRows.Count)
					return DefaultRowHeight;

				var flatRow = _dataSource.FlatRows[(int)row];

				View view;
				if (flatRow.Kind == RowKind.Header)
				{
					view = _listView.HeaderFor(flatRow.Section);
				}
				else
				{
					view = _listView.ViewFor(flatRow.Section, flatRow.Index);
				}

				if (view is null)
					return DefaultRowHeight;

				// Fast path: explicit frame constraints
				var constraints = view.GetFrameConstraints();
				if (constraints?.Height is not null)
					return (nfloat)constraints.Height;

				// Slow path: realize the view and measure
				try
				{
					if (view.ToMacOSPlatform(_mauiContext) is not null)
					{
						var measure = view.Measure(tableView.Bounds.Width, double.PositiveInfinity);
						if (measure.Height > 0)
							return (nfloat)measure.Height;
					}
				}
				catch
				{
					// Measurement can fail if handler isn't ready
				}

				return DefaultRowHeight;
			}

			public override void SelectionDidChange(NSNotification notification)
			{
				if (_listView is null)
					return;

				var tableView = notification.Object as NSTableView;
				if (tableView is null)
					return;

				var selectedRow = tableView.SelectedRow;
				if (selectedRow < 0 || selectedRow >= _dataSource.FlatRows.Count)
					return;

				var flatRow = _dataSource.FlatRows[(int)selectedRow];
				if (flatRow.Kind == RowKind.Item)
				{
					_listView.OnSelected(flatRow.Section, flatRow.Index);
				}

				// Deselect after callback, matching iOS behavior
				tableView.DeselectRow(selectedRow);
			}
		}

		// Hosts a CometNSView inside an NSTableCellView for view recycling
		class CometNSTableCellView : NSTableCellView
		{
			readonly CometNSView _cometView;

			public CometNSTableCellView(IMauiContext mauiContext)
			{
				_cometView = new CometNSView(mauiContext)
				{
					AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
				};
				AddSubview(_cometView);
			}

			public void SetView(View view)
			{
				var previousView = _cometView.CurrentView as View;
				_cometView.CurrentView = view;
				previousView?.ViewDidDisappear();
				view?.ViewDidAppear();
			}

			public override void Layout()
			{
				base.Layout();
				_cometView.Frame = Bounds;
			}
		}
	}
}
