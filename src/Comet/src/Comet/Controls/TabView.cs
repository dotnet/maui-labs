using System;
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// Simple TabView for organizing content into tabs.
	/// For navigation-based tabs, use CometShell instead.
	/// </summary>
	public class TabView : ContainerView
	{
		private List<TabItem> _tabs = new();
		private int _selectedIndex = 0;
		private Action<int> _selectedIndexChanged;

		public TabView() : base()
		{
		}

		/// <summary>
		/// Get or set the currently selected tab index
		/// </summary>
		public int SelectedIndex
		{
			get => _selectedIndex;
			set
			{
				// Allow setting before tabs are added (deferred apply).
				// Guard against the larger of _tabs and children count.
				var count = Math.Max(_tabs.Count, ((IList<View>)this).Count);
				if (value >= 0 && (count == 0 || value < count) && _selectedIndex != value)
				{
					_selectedIndex = value;
					OnSelectedIndexChanged(value);
					_selectedIndexChanged?.Invoke(value);
				}
			}
		}

		/// <summary>
		/// Raised when the selected tab changes
		/// </summary>
		public Action<int> SelectedIndexChanged
		{
			get => _selectedIndexChanged;
			set => _selectedIndexChanged = value;
		}

		/// <summary>
		/// Add a tab with title and content
		/// </summary>
		public void AddTab(string title, View content)
		{
			_tabs.Add(new TabItem { Title = title, Content = content });
			content.SetEnvironment(EnvironmentKeys.TabView.Title, title);
			Add(content);
		}

		/// <summary>
		/// Get the currently selected tab
		/// </summary>
		public TabItem CurrentTab => SelectedIndex >= 0 && SelectedIndex < _tabs.Count ? _tabs[SelectedIndex] : null;

		/// <summary>
		/// Get all tabs
		/// </summary>
		public IReadOnlyList<TabItem> Tabs => _tabs.AsReadOnly();

		protected virtual void OnSelectedIndexChanged(int index)
		{
			// Override in subclasses to update UI
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_tabs.Clear();
				_selectedIndexChanged = null;
			}
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// Represents a single tab in a TabView
	/// </summary>
	public class TabItem
	{
		/// <summary>
		/// Title displayed on the tab
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Content view for this tab
		/// </summary>
		public View Content { get; set; }

		/// <summary>
		/// Icon for this tab (optional)
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Badge value (e.g., unread count)
		/// </summary>
		public string BadgeValue { get; set; }
	}
}
