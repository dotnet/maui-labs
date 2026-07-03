#nullable enable
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	/// <summary>Material window width class — the M3 breakpoints adaptive layouts key off
	/// (Compact &lt; 600dp ≤ Medium &lt; 840dp ≤ Expanded).</summary>
	public enum WindowWidthClass { Compact, Medium, Expanded }

	/// <summary>Material window height class (Compact &lt; 480dp ≤ Medium &lt; 900dp ≤ Expanded).</summary>
	public enum WindowHeightClass { Compact, Medium, Expanded }

	/// <summary>
	/// Per-root reactive window geometry: the root view's available size in Dp as a
	/// <see cref="Signal{T}"/>, plus the Material window-size-class derivations adaptive
	/// UIs switch on (Reply's list-detail / navigation-suite chrome). Reading
	/// <see cref="SizeDp"/>, <see cref="WidthClass"/>, or <see cref="HeightClass"/> inside
	/// a body/binding tracks the dependency, so a resize (rotation, split-screen, soft
	/// keyboard, desktop window drag) re-renders exactly the views that read it.
	/// </summary>
	/// <remarks>
	/// The backend root that hosts a view tree owns one instance and calls
	/// <see cref="Update"/> whenever its native view's laid-out size changes. Views resolve
	/// theirs via <see cref="WindowMetricsExtensions.GetWindowMetrics"/> — environment first
	/// (a root installs its instance with <see cref="WindowMetricsExtensions.WindowMetrics{T}"/>),
	/// falling back to <see cref="Shared"/>, the single-window default kept current by
	/// today's Compose/SwiftUI roots. This replaces the process-wide static
	/// <c>ComposeNode.AvailableSize</c> as the contract adaptive views program against;
	/// the platform roots keep <see cref="Shared"/> current (full re-plumb of the statics
	/// lands with the first adaptive sample).
	/// </remarks>
	public sealed class CometWindowMetrics
	{
		/// <summary>The single-window default. Backend roots keep it current; views that
		/// resolve no per-root instance from their environment read this one.</summary>
		public static CometWindowMetrics Shared { get; } = new();

		/// <summary>The root's available size in Dp. Zero until the first native layout —
		/// consumers treating zero as "unknown" should defer or use a platform fallback.</summary>
		public Signal<Size> SizeDp { get; } = new(Size.Zero);

		/// <summary>Reactive Material width class of the current size.</summary>
		public WindowWidthClass WidthClass => ClassifyWidth(SizeDp.Value.Width);

		/// <summary>Reactive Material height class of the current size.</summary>
		public WindowHeightClass HeightClass => ClassifyHeight(SizeDp.Value.Height);

		/// <summary>Called by the owning backend root when the native root view's laid-out
		/// size changes. No-ops on equal sizes (Signal's equality gate), so per-frame
		/// layout callbacks are safe to forward directly.</summary>
		public void Update(Size sizeDp) => SizeDp.Value = sizeDp;

		/// <summary>M3 width breakpoints: Compact &lt; 600 ≤ Medium &lt; 840 ≤ Expanded.</summary>
		public static WindowWidthClass ClassifyWidth(double widthDp) =>
			widthDp < 600 ? WindowWidthClass.Compact
			: widthDp < 840 ? WindowWidthClass.Medium
			: WindowWidthClass.Expanded;

		/// <summary>M3 height breakpoints: Compact &lt; 480 ≤ Medium &lt; 900 ≤ Expanded.</summary>
		public static WindowHeightClass ClassifyHeight(double heightDp) =>
			heightDp < 480 ? WindowHeightClass.Compact
			: heightDp < 900 ? WindowHeightClass.Medium
			: WindowHeightClass.Expanded;
	}

	public static class WindowMetricsExtensions
	{
		internal const string EnvironmentKey = "Comet.WindowMetrics";

		/// <summary>Installs a per-root <see cref="CometWindowMetrics"/> into the view's
		/// environment (cascades to descendants). Called by a backend root on its root view.</summary>
		public static T WindowMetrics<T>(this T view, CometWindowMetrics metrics) where T : View
			=> view.SetEnvironment(EnvironmentKey, metrics, cascades: true);

		/// <summary>The window metrics governing this view: the nearest environment-installed
		/// instance, else <see cref="CometWindowMetrics.Shared"/>.</summary>
		public static CometWindowMetrics GetWindowMetrics(this View view)
			=> view.GetEnvironment<CometWindowMetrics>(EnvironmentKey) ?? CometWindowMetrics.Shared;
	}
}
