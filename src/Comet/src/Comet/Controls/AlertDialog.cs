#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Comet.Reactive;

namespace Comet
{
	internal static class AlertDialogSlotOwnership
	{
		static readonly object Gate = new();
		static readonly ConditionalWeakTable<View, WeakReference<AlertDialog>> Owners = new();

		public static void Claim(AlertDialog owner, View? slot)
		{
			if (slot is null)
				return;

			lock (Gate)
			{
				Owners.Remove(slot);
				Owners.Add(slot, new WeakReference<AlertDialog>(owner));
			}
			slot.Parent = owner;
		}

		public static bool TryRelease(AlertDialog owner, View slot)
		{
			lock (Gate)
			{
				if (!Owners.TryGetValue(slot, out var reference) ||
					!reference.TryGetTarget(out var currentOwner) ||
					!ReferenceEquals(currentOwner, owner))
					return false;
				Owners.Remove(slot);
			}

			if (!ReferenceEquals(slot.Parent, owner))
				return false;
			slot.Parent = null;
			return true;
		}
	}

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

			AlertDialogSlotOwnership.Claim(this, text);
			AlertDialogSlotOwnership.Claim(this, confirmButton);
			AlertDialogSlotOwnership.Claim(this, title);
			AlertDialogSlotOwnership.Claim(this, dismissButton);
		}

		/// <summary>Drives visibility: the dialog composes while true and dismisses (writes false)
		/// itself when the user taps the scrim / presses back.</summary>
		public Signal<bool> IsOpen { get; private set; }

		/// <summary>The body text slot (Material <c>text</c> slot). Required.</summary>
		public View Text { get; private set; }

		/// <summary>The confirm-action slot (Material <c>confirmButton</c>, e.g. a "CLOSE" TextButton).
		/// Required.</summary>
		public View ConfirmButton { get; private set; }

		/// <summary>Optional headline slot (Material <c>title</c>).</summary>
		public View? Title { get; private set; }

		/// <summary>Optional secondary-action slot (Material <c>dismissButton</c>).</summary>
		public View? DismissButton { get; private set; }

		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View>();
			if (Text is not null) children.Add(Text);
			if (ConfirmButton is not null) children.Add(ConfirmButton);
			if (Title is not null) children.Add(Title);
			if (DismissButton is not null) children.Add(DismissButton);
			return children;
		}

		internal override void AdoptRetainedIdentityStateFrom(View replacement)
		{
			var dialog = (AlertDialog)replacement;
			var outgoingSlots = GetChildren();
			var incomingSlots = dialog.GetChildren();
			var wasHooked = _hooked;

			if (wasHooked)
				IsOpen.PropertyChanged -= OnOpenChanged;

			IsOpen = dialog.IsOpen;
			Text = dialog.Text;
			ConfirmButton = dialog.ConfirmButton;
			Title = dialog.Title;
			DismissButton = dialog.DismissButton;
			dialog.Text = null!;
			dialog.ConfirmButton = null!;
			dialog.Title = null;
			dialog.DismissButton = null;

			foreach (var slot in incomingSlots)
				AlertDialogSlotOwnership.Claim(this, slot);

			if (wasHooked)
				IsOpen.PropertyChanged += OnOpenChanged;

			base.AdoptRetainedIdentityStateFrom(replacement);
			DisposeOwnedSlots(outgoingSlots, incomingSlots);
		}

		void DisposeOwnedSlots(
			IReadOnlyList<View> slots,
			IReadOnlyList<View>? retainedSlots = null)
		{
			var disposed = new HashSet<View>();
			foreach (var slot in slots)
			{
				if (slot is null ||
					!disposed.Add(slot))
					continue;
				if (retainedSlots is not null)
				{
					var retained = false;
					foreach (var current in retainedSlots)
					{
						if (!ReferenceEquals(slot, current))
							continue;
						retained = true;
						break;
					}
					if (retained)
						continue;
				}

				if (AlertDialogSlotOwnership.TryRelease(this, slot))
					slot.Dispose();
			}
		}

	}
}
