#nullable enable
using System.Collections.Generic;
using Comet;

namespace CometSamples.Jetsnack
{
	/// <summary>Jetsnack's Material Icons glyph map (the shared one-font approach).</summary>
	static class JetsnackIcons
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

			Add("expand_more", 0xE5CF);     // destination bar
			Add("filter_list", 0xE152);     // filter bar
			Add("arrow_forward", 0xE5C8);   // section header (gold ic_arrow_back mirrored)
			Add("arrow_back", 0xE5C4);      // detail up
			Add("home", 0xE88A);            // bottom bar
			Add("search", 0xE8B6);
			Add("shopping_cart", 0xE8CC);
			Add("account_circle", 0xE853);
			Add("add", 0xE145);             // qty stepper
			Add("remove", 0xE15B);
			Add("close", 0xE5CD);           // filters sheet / cart remove
			Add("star", 0xE838);            // sort filter
			Add("sort_by_alpha", 0xE053);
			Add("android", 0xE859);

			IconFont.Register(Font, map);
		}
	}
}
