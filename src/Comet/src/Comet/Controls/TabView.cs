#nullable enable
using System;
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// A bottom-tab view that drives REAL platform controls: Material 3
	/// <c>NavigationBar</c> + <c>NavigationBarItem</c> on Compose, and a native
	/// bottom tab bar on SwiftUI. Selection is reactive (Signal-driven) so sibling
	/// content bound to the same signal re-renders on tab change.
	/// </summary>
	public partial class TabView : ContainerView
	{
		private List<TabItem> _tabs = new();
		private readonly Signal<int> _selectedSignal = new(0);
		private Action<int>? _selectedIndexChanged;

		public TabView() : base()
		{
		}

		/// <summary>
		/// Reactive signal driving the selected tab index. Platform backend nodes
		/// subscribe to this signal and highlight the active tab natively.
		/// </summary>
		public Signal<int> SelectedSignal => _selectedSignal;

		/// <summary>
		/// Get or set the currently selected tab index.
		/// </summary>
		public int SelectedIndex
		{
			get => _selectedSignal.Value;
			set
			{
				var count = Math.Max(_tabs.Count, ((IList<View>)this).Count);
				if (value >= 0 && (count == 0 || value < count) && _selectedSignal.Value != value)
				{
					_selectedSignal.Value = value;
					OnSelectedIndexChanged(value);
					_selectedIndexChanged?.Invoke(value);
				}
			}
		}

		/// <summary>
		/// Raised when the selected tab changes.
		/// </summary>
		public Action<int>? SelectedIndexChanged
		{
			get => _selectedIndexChanged;
			set => _selectedIndexChanged = value;
		}

		/// <summary>
		/// Adds a tab without an icon. Retained for binary compatibility with the
		/// original two-parameter API.
		/// </summary>
		public void AddTab(string title, View content)
			=> AddTab(title, content, null);

		/// <summary>
		/// Add a tab with title, icon, and content. The icon name maps to the
		/// platform's icon system (Material Symbols on Compose, SF Symbols on iOS).
		/// </summary>
		public void AddTab(string title, View content, string? icon)
		{
			_tabs.Add(new TabItem { Title = title, Content = content, Icon = icon });
			content.SetEnvironment(EnvironmentKeys.TabView.Title, title);
			if (icon is not null)
				content.SetEnvironment(EnvironmentKeys.TabView.Image, (object)icon);
			Add(content);
		}

		/// <summary>
		/// Selection entry point for platform nodes: writes the signal and fires
		/// the callback. Matches the <see cref="NavigationBar.SelectItem"/> contract.
		/// </summary>
		public void SelectItem(int index)
		{
			var count = Math.Max(_tabs.Count, ((IList<View>)this).Count);
			if (index >= 0 && (count == 0 || index < count))
			{
				_selectedSignal.Value = index;
				OnSelectedIndexChanged(index);
				_selectedIndexChanged?.Invoke(index);
			}
		}

		/// <summary>
		/// Get the currently selected tab.
		/// </summary>
		public TabItem? CurrentTab => SelectedIndex >= 0 && SelectedIndex < _tabs.Count ? _tabs[SelectedIndex] : null;

		/// <summary>
		/// Get all tabs.
		/// </summary>
		public IReadOnlyList<TabItem> Tabs => _tabs.AsReadOnly();

		protected virtual void OnSelectedIndexChanged(int index)
		{
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				DetachSelectedSignalSubscription();
				_tabs.Clear();
				_selectedIndexChanged = null;
			}
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// Represents a single tab in a TabView.
	/// </summary>
	public class TabItem
	{
		/// <summary>Title displayed on the tab.</summary>
		public string Title { get; set; } = string.Empty;

		/// <summary>Content view for this tab.</summary>
		public View? Content { get; set; }

		/// <summary>
		/// Icon name for this tab. Maps to the platform icon system
		/// (Material Symbols on Compose, SF Symbols on iOS).
		/// </summary>
		public string? Icon { get; set; }

		/// <summary>Badge value (e.g., unread count).</summary>
		public string? BadgeValue { get; set; }
	}
}
