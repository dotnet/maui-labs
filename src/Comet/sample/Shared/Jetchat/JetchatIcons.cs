#nullable enable
using System.Collections.Generic;
using Comet;

namespace CometSamples.Jetchat
{
	/// <summary>
	/// Registers Google's Material Icons font as Comet's icon font, so every <c>Icon("name")</c> in
	/// the shared tree renders the SAME Material glyph on iOS and Android (the cross-platform Material
	/// design choice — one font, one glyph set, fully tintable). The font file itself is loaded per
	/// platform first (Android <c>ComposeFontRegistry</c>; iOS <c>UIAppFonts</c>) under this family.
	/// The "jetchat" brand logo is intentionally NOT mapped — it falls back to its bundled asset.
	/// </summary>
	static class JetchatIcons
	{
		public const string Font = "Material Icons";

		// Cross-platform name → Material Icons codepoint. Names match the gold's ic_* drawables:
		// @ = alternate_email, photo = insert_photo, video = duo, arrow_down = arrow_downward.
		public static void Register()
		{
			var map = new Dictionary<string, string>();
			void Add(string name, int codepoint) => map[name] = char.ConvertFromUtf32(codepoint);

			Add("search", 0xE8B6);
			Add("info", 0xE88E);
			Add("mood", 0xE7F2);
			Add("at", 0xE0E6);          // alternate_email
			Add("photo", 0xE251);       // insert_photo
			Add("place", 0xE55F);
			Add("video", 0xE9A5);       // duo
			Add("mic", 0xE029);
			Add("arrow_down", 0xE5DB);  // arrow_downward
			Add("create", 0xE150);
			Add("chat", 0xE0B7);
			Add("send", 0xE163);

			IconFont.Register(Font, map);
		}
	}
}
