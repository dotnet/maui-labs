#nullable enable
using System;
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// One destination inside a <see cref="NavigationBar"/> or <see cref="NavigationRail"/>.
	/// <see cref="IconView"/> / <see cref="LabelView"/> are app-styled views passed to the real
	/// Material 3 item composable's icon/label slots (leave colours unset to inherit the item's
	/// selected/unselected content colours). Not a standalone renderable — the parent chrome
	/// node drives the platform item widget and invokes <see cref="OnSelect"/> on tap.
	/// </summary>
	public partial class NavigationItem : View, IContainerView
	{
		public NavigationItem(View icon, View? label = null, Action? onSelect = null)
		{
			IconView = icon;
			LabelView = label;
			OnSelect = onSelect;
			icon.Parent = this;
			if (label is not null)
				label.Parent = this;
		}

		public View IconView { get; }
		public View? LabelView { get; }
		public Action? OnSelect { get; }

		public IReadOnlyList<View> GetChildren()
			=> LabelView is null ? new[] { IconView } : new[] { IconView, LabelView };
	}
}
