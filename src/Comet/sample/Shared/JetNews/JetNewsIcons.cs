#nullable enable
using System.Collections.Generic;
using Comet;

namespace CometSamples.JetNews
{
	/// <summary>
	/// JetNews' Material Icons glyph map (the one-font cross-platform approach shared by
	/// Jetchat/Reply). Names follow the gold's icon usages (utils/JetnewsIcons.kt + the
	/// material icons the screens reference directly).
	/// </summary>
	static class JetNewsIcons
	{
		public const string Font = "Material Icons";

		static bool _registered;

		public static void Register()
		{
			if (_registered)
				return;
			_registered = true;

			var map = new Dictionary<string, string>();
			void Add(string name, int codepoint) => map[name] = char.ConvertFromUtf32(codepoint);

			Add("menu", 0xE5D2);              // drawer affordance (gold uses the brand icon image; menu as fallback)
			Add("search", 0xE8B6);            // app bar search
			Add("arrow_back", 0xE5C4);        // article top bar
			Add("home", 0xE88A);              // drawer Home
			Add("list_alt", 0xE0EE);          // drawer Interests (ListAlt)
			Add("bookmark", 0xE866);          // saved post (filled)
			Add("bookmark_border", 0xE867);   // unsaved post
			Add("thumb_up_offalt", 0xE8DC);   // article action bar like (ThumbUpOffAlt)
			Add("share", 0xE80D);             // article action bar share
			Add("text_format", 0xE165);       // article action bar text settings (FormatSize glyph)
			Add("more_vert", 0xE5D4);         // history row overflow
			Add("add", 0xE145);               // interests unselected toggle
			Add("check", 0xE5CA);             // interests selected toggle
			Add("account_circle", 0xE853);    // article author avatar placeholder
			Add("android", 0xE859);           // publication badge stand-in (gold: icon_post_background vector)

			IconFont.Register(Font, map);
		}
	}
}
