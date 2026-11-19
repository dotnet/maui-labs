#nullable enable
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// A Material 3 icon button (Compose <c>IconButton</c>) — the gold's standard
	/// icon-action control (Jetcaster's queue-add / overflow, JetNews' app-bar
	/// actions): a 48dp tap target with a bounded state layer around a single icon
	/// slot. Promoted from the M3 review backlog — a styled Icon+OnTap look-alike
	/// is a defect under the exact-widget rule.
	/// </summary>
	public partial class IconButton : View, IContainerView
	{
		public IconButton(System.Action onClick, View icon)
		{
			OnClick = onClick;
			IconView = icon;
			icon.Parent = this;
		}

		public System.Action OnClick { get; }
		public View IconView { get; }

		public IReadOnlyList<View> GetChildren() => new[] { IconView };
	}
}
