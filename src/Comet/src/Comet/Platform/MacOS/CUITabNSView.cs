using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
namespace Comet.MacOS
{
	public class CUITabNSView : NSView
	{
		readonly NSSegmentedControl _tabBar;
		readonly NSView _contentArea;
		NSView _currentPageView;
		TabView _tabView;
		readonly List<IView> _tabs = new();

		public IMauiContext Context { get; set; }

		public CUITabNSView()
		{
			WantsLayer = true;

			_tabBar = new NSSegmentedControl
			{
				SegmentStyle = NSSegmentStyle.Automatic,
				TrackingMode = NSSegmentSwitchTracking.SelectOne,
			};
			_tabBar.Activated += OnTabSelected;

			_contentArea = new NSView { WantsLayer = true };

			AddSubview(_tabBar);
			AddSubview(_contentArea);
		}

		public void Setup(TabView tabView)
		{
			_tabView = tabView;
			_tabs.Clear();

			if (tabView is null)
				return;

			foreach (var tab in tabView)
			{
				_tabs.Add(tab);
			}

			_tabBar.SegmentCount = _tabs.Count;
			for (int i = 0; i < _tabs.Count; i++)
			{
				var title = (_tabs[i] as View)?.GetTitle() ?? $"Tab {i}";
				_tabBar.SetLabel(title, i);
				_tabBar.SetWidth(0, i);
			}

			if (_tabs.Count > 0)
				SelectTab(0);
		}

		void OnTabSelected(object sender, EventArgs e)
		{
			SelectTab((int)_tabBar.SelectedSegment);
		}

		void SelectTab(int index)
		{
			if (index < 0 || index >= _tabs.Count || Context is null)
				return;

			_tabBar.SelectedSegment = index;
			_currentPageView?.RemoveFromSuperview();

			var tab = _tabs[index];
			var platformView = tab.ToMacOSPlatform(Context);
			_currentPageView = platformView;
			_contentArea.AddSubview(platformView);
			NeedsLayout = true;
		}

		public override void Layout()
		{
			base.Layout();
			var bounds = Bounds;
			if (bounds.Width <= 0 || bounds.Height <= 0)
				return;

			var tabBarHeight = (nfloat)30;
			var padding = (nfloat)8;

			var tabSize = _tabBar.FittingSize;
			var tabX = (bounds.Width - tabSize.Width) / 2;
			_tabBar.Frame = new CGRect(tabX, padding, tabSize.Width, tabBarHeight);

			var contentTop = padding + tabBarHeight + padding;
			_contentArea.Frame = new CGRect(0, contentTop, bounds.Width, bounds.Height - contentTop);

			if (_currentPageView is not null)
			{
				_currentPageView.Frame = _contentArea.Bounds;
				var tabIndex = (int)_tabBar.SelectedSegment;
				if (tabIndex >= 0 && tabIndex < _tabs.Count)
				{
					var tab = _tabs[tabIndex];
					tab.Measure(
						(double)_contentArea.Bounds.Width,
						(double)_contentArea.Bounds.Height);
					tab.Arrange(new Microsoft.Maui.Graphics.Rect(
						0, 0,
						(double)_contentArea.Bounds.Width,
						(double)_contentArea.Bounds.Height));
				}
			}
		}
	}
}
