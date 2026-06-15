#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A Material 3 floating action button. Drives the REAL Compose
	/// <c>FloatingActionButton</c> — or <c>ExtendedFloatingActionButton</c> when
	/// <see cref="Extended"/> — never a styled pill. The backend treats it as a self-sizing native
	/// overlay (Option C): it measures the content's intrinsic size for the parent's Yoga layout,
	/// then positions the FAB at that offset and lets the native control size + lay out its own
	/// content (pinning only the gold's <see cref="Height"/>). <see cref="IconView"/> and
	/// <see cref="LabelView"/> are app-styled views (so the label uses the app's font); leave their
	/// colour unset to inherit the FAB's <see cref="ContentColor"/>.
	/// </summary>
	public partial class Fab : View, IContainerView
	{
		public Fab(View icon, View label, Action onClick, double height,
			Color? containerColor = null, Color? contentColor = null, bool extended = false)
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

		/// <summary>True → the animated <c>ExtendedFloatingActionButton</c> (icon + text slots);
		/// false → a regular <c>FloatingActionButton</c> with an icon+text row as its content.</summary>
		public bool Extended { get; }

		public IReadOnlyList<View> GetChildren() => new[] { IconView, LabelView };
	}
}
