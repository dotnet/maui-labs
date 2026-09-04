#nullable enable
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// A Material 3 filter chip (Compose <c>FilterChip</c>) — the Jetcaster category
	/// tabs: a selectable pill whose label reflects the selected state (the widget
	/// self-themes the fill, check mark, and outline). Taps report through
	/// <paramref name="onClick"/>; rows typically rebuild with the new state.
	/// </summary>
	public partial class FilterChip : View, IContainerView
	{
		public FilterChip(bool selected, System.Action onClick, View label, View? leadingIcon = null)
		{
			IsSelected = selected;
			OnClick = onClick;
			LabelView = label;
			LeadingIconView = leadingIcon;
			label.Parent = this;
			if (leadingIcon is not null)
				leadingIcon.Parent = this;
		}

		public bool IsSelected { get; }
		public System.Action OnClick { get; }
		public View LabelView { get; }
		public View? LeadingIconView { get; }

		public IReadOnlyList<View> GetChildren() =>
			LeadingIconView is null ? new[] { LabelView } : new[] { LabelView, LeadingIconView };
	}
}
