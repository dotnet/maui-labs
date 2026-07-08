#nullable enable
using System;
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// A Material 3 docked search bar: drives the REAL Compose state-based
	/// <c>SearchBar</c> + <c>ExpandedDockedSearchBar</c> pair (the successor of the gold's
	/// <c>DockedSearchBar</c> — expansion animates on input-field focus, back collapses,
	/// both handled inside the widget). The typed text flows OUT reactively through
	/// <see cref="Query"/>; put the results UI in <see cref="ContentView"/> and read the
	/// signal there (a bound status text + a <see cref="ListView"/> reloaded on change).
	/// Slot views (placeholder/leading/trailing) are app-styled Comet views.
	/// </summary>
	public partial class SearchBar : View, IContainerView
	{
		public SearchBar(Signal<string> query, View placeholder, View content,
			View? leading = null, View? trailing = null, Action<string>? onSearch = null)
		{
			Query = query;
			PlaceholderView = placeholder;
			ContentView = content;
			LeadingView = leading;
			TrailingView = trailing;
			OnSearch = onSearch;
			placeholder.Parent = this;
			content.Parent = this;
			if (leading is not null)
				leading.Parent = this;
			if (trailing is not null)
				trailing.Parent = this;
		}

		/// <summary>The live query text — the node writes every edit into it (equality-gated),
		/// so results UI that reads it re-renders per keystroke.</summary>
		public Signal<string> Query { get; }
		public View PlaceholderView { get; }
		public View ContentView { get; }
		public View? LeadingView { get; }
		public View? TrailingView { get; }
		/// <summary>Invoked on the IME Search action with the current text.</summary>
		public Action<string>? OnSearch { get; }

		/// <summary>Whether the bar is expanded (pane open). Lives on the CONTROL, not the
		/// backend node: an ancestor own-content refresh re-materializes the node, and
		/// node-local expansion state would silently collapse the pane.</summary>
		public Signal<bool> Expanded { get; } = new(false);

		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View> { PlaceholderView, ContentView };
			if (LeadingView is not null)
				children.Add(LeadingView);
			if (TrailingView is not null)
				children.Add(TrailingView);
			return children;
		}

		/// <summary>Programmatic text entry (the dev agent's fill action targeting the
		/// SearchBar element): drives the SAME pipeline as typing — expands the bar and
		/// writes the query signal. On Android the input field is a native M3 facade widget
		/// (not a registered Comet element), so this is the only agent-reachable path there.</summary>
		protected internal override void OnBackendEvent<T>(Backend.EventId id, T payload)
		{
			if (id == Backend.EventIds.TextChanged && payload is string s)
			{
				Expanded.Value = true;
				Query.Value = s;
			}
		}
	}
}
