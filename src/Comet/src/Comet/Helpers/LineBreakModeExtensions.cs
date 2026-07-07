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
	}
}
