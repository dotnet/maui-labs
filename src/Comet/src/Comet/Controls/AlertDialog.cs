#nullable enable
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// A Material 3 <c>AlertDialog</c>: a modal popup (its own overlay window + scrim) shown while
	/// <see cref="IsOpen"/> is true. Maps to the platform's real dialog — Compose
	/// <c>AlertDialog</c> (the SwiftUI alert is a follow-up). Mirrors Jetchat's
	/// <c>FunctionalityNotAvailablePopup</c>. The slot views (<see cref="Text"/>, optional
	/// <see cref="Title"/>, and the buttons) are ordinary Comet views laid out by Material inside the
	/// dialog; a scrim tap / back press writes <see cref="IsOpen"/> back to false. The dialog occupies
	/// zero space in its parent layout (the backend node manages its own content), so it can sit
	/// anywhere in the tree — its position there doesn't affect where it appears on screen.
	/// </summary>
	public partial class AlertDialog : View, IContainerView
	{
		public AlertDialog(Signal<bool> isOpen, View text, View confirmButton, View? title = null, View? dismissButton = null)
		{
			IsOpen = isOpen;
			Text = text;
			ConfirmButton = confirmButton;
			Title = title;
			DismissButton = dismissButton;

			text.Parent = this;
			confirmButton.Parent = this;
			if (title is not null) title.Parent = this;
			if (dismissButton is not null) dismissButton.Parent = this;
		}

		/// <summary>Drives visibility: the dialog composes while true and dismisses (writes false)
		/// itself when the user taps the scrim / presses back.</summary>
		public Signal<bool> IsOpen { get; }

		/// <summary>The body text slot (Material <c>text</c> slot). Required.</summary>
		public View Text { get; }

		/// <summary>The confirm-action slot (Material <c>confirmButton</c>, e.g. a "CLOSE" TextButton).
		/// Required.</summary>
		public View ConfirmButton { get; }

		/// <summary>Optional headline slot (Material <c>title</c>).</summary>
		public View? Title { get; }

		/// <summary>Optional secondary-action slot (Material <c>dismissButton</c>).</summary>
		public View? DismissButton { get; }

		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View> { Text, ConfirmButton };
			if (Title is not null) children.Add(Title);
			if (DismissButton is not null) children.Add(DismissButton);
			return children;
		}
	}
}
