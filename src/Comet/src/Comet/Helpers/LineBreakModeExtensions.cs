using System;
using Microsoft.Maui;

namespace Comet
{
	public static class LineBreakModeExtensions
	{
		public static LineBreakMode GetLineBreakMode<T>(this T view, LineBreakMode defaultMode) where T : View
		{
			var mode = view.GetEnvironment<LineBreakMode?>(EnvironmentKeys.LineBreakMode.Mode);
			return mode ?? defaultMode;
		}

		public static T LineBreakMode<T>(this T view, LineBreakMode mode) where T : View =>
			view.SetEnvironment(EnvironmentKeys.LineBreakMode.Mode, (object)mode);
		/// <summary>Clamp the text to at most <paramref name="lines"/> lines; overflow renders
		/// an ellipsis (Compose Text maxLines + TextOverflow.Ellipsis). 0 = unlimited.</summary>
		public static T MaxLines<T>(this T view, int lines) where T : View =>
			view.SetEnvironment(EnvironmentKeys.Text.MaxLines, lines);

		public static T LineBreakMode<T>(this T view, Func<LineBreakMode> mode) where T : View =>
			view.LineBreakMode(mode());

		/// <summary>Wrap STRATEGY (where soft breaks fall), distinct from LineBreakMode
		/// (wrap vs truncate): Heading balances lines at phrase boundaries, Paragraph uses
		/// high-quality (non-greedy) breaking — Compose's LineBreak presets. Android drives
		/// the real Compose lineBreak; SwiftUI has no public wrap-strategy API, so iOS keeps
		/// greedy wrapping (documented deviation).</summary>
		public static T LineBreak<T>(this T view, TextLineBreak strategy) where T : View =>
			view.SetEnvironment(EnvironmentKeys.Text.LineBreak, strategy);
	}

	/// <summary>Soft-wrap strategy presets mirroring Compose's <c>LineBreak</c>.</summary>
	public enum TextLineBreak
	{
		/// <summary>Greedy wrapping (the platform default).</summary>
		Default = 0,
		/// <summary>Balanced lines, phrase-boundary breaks — for titles.</summary>
		Heading = 1,
		/// <summary>High-quality non-greedy wrapping — for body copy.</summary>
		Paragraph = 2,
	}
}
