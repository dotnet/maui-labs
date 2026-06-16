#nullable enable
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// An icon font (e.g. Google's Material Icons): a single font whose glyphs are addressed by
	/// codepoint, so <c>Icon("name")</c> renders the SAME glyph on every backend instead of each
	/// platform's own icon set. Register the family + a name→glyph(codepoint) map once at startup;
	/// any <see cref="Icon"/> whose symbol is mapped renders that glyph in this font, and unmapped
	/// symbols (e.g. a brand logo) fall back to the platform-native icon path.
	/// </summary>
	/// <remarks>
	/// Codepoints (not ligatures) are used so rendering doesn't depend on the text engine enabling
	/// the font's ligature table. The font file itself must be loaded by each platform the usual way
	/// (Android <c>ComposeFontRegistry</c>; iOS <c>UIAppFonts</c>) under the same <see cref="Family"/>.
	/// </remarks>
	public static class IconFont
	{
		static Dictionary<string, string> _map = new();

		/// <summary>The registered icon-font family name, or null if none.</summary>
		public static string? Family { get; private set; }

		/// <summary>Register the icon font and its name→glyph map (glyph = the codepoint string,
		/// e.g. "" for mic). Call after the font file is loaded under <paramref name="family"/>.</summary>
		public static void Register(string family, IDictionary<string, string> glyphs)
		{
			Family = family;
			_map = new Dictionary<string, string>(glyphs);
		}

		/// <summary>Resolves a cross-platform icon name to its glyph in the registered font.</summary>
		public static bool TryGlyph(string name, out string glyph)
		{
			if (Family is not null && _map.TryGetValue(name, out var g))
			{
				glyph = g;
				return true;
			}
			glyph = string.Empty;
			return false;
		}
	}
}
