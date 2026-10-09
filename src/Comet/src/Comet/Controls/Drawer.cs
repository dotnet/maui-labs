#nullable enable
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// A modal navigation drawer: a <see cref="Side"/> panel that slides in over the
	/// <see cref="Content"/>, dimming it. Maps to the platform's real drawer — Compose
	/// <c>ModalNavigationDrawer</c> and a SwiftUI sliding panel — driven by an
	/// <see cref="IsOpen"/> signal (set it true from a nav button; the drawer writes it back
	/// to false when dismissed by tap/swipe).
	/// </summary>
	public partial class Drawer : View, IContainerView
	{
		public Drawer(Signal<bool> isOpen, View side, View content)
		{
			IsOpen = isOpen;
			Side = side;
			Content = content;
			side.Parent = this;
			content.Parent = this;
		}

		public Signal<bool> IsOpen { get; }
		public View Side { get; }
		public View Content { get; }

		public IReadOnlyList<View> GetChildren() => new[] { Side, Content };
	}
}
