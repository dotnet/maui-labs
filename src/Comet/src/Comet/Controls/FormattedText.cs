#nullable enable
using System.Collections.Generic;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>A styled run within a <see cref="FormattedText"/> — a slice of text with optional
	/// color, a monospace ("code") face, a background highlight, and an underline (link).</summary>
	public sealed record TextRun(
		string Text,
		Color? Color = null,
		bool Monospace = false,
		Color? Background = null,
		bool Underline = false);

	/// <summary>
	/// Inline rich text: a single wrapping paragraph composed of styled <see cref="TextRun"/>s
	/// (the Comet analog of a Compose <c>AnnotatedString</c> / an attributed string). Used for chat
	/// bubbles that style @mentions, <c>code</c> spans, and links. The base font (size/family) is
	/// set with the usual <c>.FontSize()</c>/<c>.FontFamily()</c>; per-run overrides win.
	/// </summary>
	public partial class FormattedText : View
	{
		public FormattedText(IReadOnlyList<TextRun> runs) => Runs = runs;

		public IReadOnlyList<TextRun> Runs { get; }
	}
}
