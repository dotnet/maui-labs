#nullable enable
using System.Collections.Generic;
using Comet;

namespace CometSamples.Reply
{
	/// <summary>
	/// Reply's Material Icons glyph map (same one-font cross-platform approach as
	/// <c>JetchatIcons</c>). Names match the gold's ic_* drawables in
	/// compose-samples/Reply/app/src/main/res/drawable/.
	/// </summary>
	static class ReplyIcons
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

			Add("inbox", 0xE156);
			Add("article", 0xEF42);
			Add("chat_outline", 0xE0CB);   // ic_chat: chat_bubble_outline
			Add("people_outline", 0xE7FC); // ic_group
			Add("menu", 0xE5D2);
			Add("menu_open", 0xE9BD);
			Add("edit", 0xE3C9);           // ic_edit (compose)
			Add("search", 0xE8B6);
			Add("arrow_back", 0xE5C4);
			Add("more_vert", 0xE5D4);
			Add("star_border", 0xE83A);
			Add("check", 0xE5CA);

			IconFont.Register(Font, map);
		}
	}
}
