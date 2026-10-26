#nullable enable
using System;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A Material 3 floating action button. Drives the REAL Compose
	/// <c>ExtendedFloatingActionButton</c> — never a styled pill. The backend treats it as a
	/// self-sizing native overlay (Option C): it measures the content's intrinsic size for the
	/// parent's Yoga layout, then positions the FAB at that offset and lets the native control
	/// size + lay out its own content. <see cref="IconView"/> and <see cref="LabelView"/> are
	/// app-styled views (so the label uses the app's font); leave their colour unset to inherit
	/// the FAB's <see cref="ContentColor"/>.
	/// </summary>
	public partial class Fab : View, IContainerView
	{
		public Fab(View icon, View label, Action onClick, double height,
			Color? containerColor = null, Color? contentColor = null, bool extended = true)
		{
			IconView = icon;
			LabelView = label;
			Clicked = onClick;
			Height = height;
			ContainerColor = containerColor;
			ContentColor = contentColor;
			Extended = extended;
			icon.Parent = this;
			label.Parent = this;
		}

		public View IconView { get; }
		public View LabelView { get; }
		public Action Clicked { get; }
		public double Height { get; }
		public Color? ContainerColor { get; }
		public Color? ContentColor { get; }

		/// <summary>Initial extended state (true = icon+label pill; false = icon-only square).
		/// Override reactively with <see cref="ExtendedWhen"/>.</summary>
		public bool Extended { get; }

		/// <summary>When set, the backend node subscribes to this signal and re-animates the FAB
		/// between its extended (true) and contracted (false) states. Drives the Material 3
		/// <c>ExtendedFloatingActionButton(expanded=…)</c> parameter on Android and a label
		/// show/hide animation on iOS.</summary>
		public Signal<bool>? ExtendedSignal { get; private set; }

		/// <summary>Wire a reactive signal that drives the extend/contract animation. Returns
		/// <c>this</c> so the call can be chained with other modifiers.</summary>
		public Fab ExtendedWhen(Signal<bool> signal) { ExtendedSignal = signal; return this; }

		public IReadOnlyList<View> GetChildren() => new[] { IconView, LabelView };
	}
}
