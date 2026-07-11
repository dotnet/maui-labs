#nullable enable
using Microsoft.Maui.Graphics;

namespace CometSamples.Jetsnack
{
	/// <summary>
	/// Jetsnack's CUSTOM design system palette, values-from-source (ui/theme/Color.kt +
	/// Theme.kt LightColorPalette). Unlike Reply/JetNews this is NOT a Material scheme —
	/// the gold provides its own JetsnackColors (brand ramps + gradient stop lists) and
	/// hand-composes its chrome from them, so the Comet sample carries the same tokens.
	/// (Light palette only — the golds are light; dark is a later pass.)
	/// </summary>
	public static class JetsnackTheme
	{
		static Color C(uint argb) => Color.FromUint(argb);

		// ── Color.kt ramps (the ones the light palette + gradients reference) ──
		public static readonly Color Shadow11 = C(0xff001787);
		public static readonly Color Shadow9 = C(0xff0009b3);
		public static readonly Color Shadow5 = C(0xff4b30ed);
		public static readonly Color Shadow4 = C(0xff7057f5);
		public static readonly Color Shadow3 = C(0xff9b86fa);
		public static readonly Color Shadow2 = C(0xffc8bbfd);
		public static readonly Color Ocean11 = C(0xff005687);
		public static readonly Color Ocean3 = C(0xff86f7fa);
		public static readonly Color Lavender3 = C(0xffc186fa);
		public static readonly Color Rose4 = C(0xfff4568b);
		public static readonly Color Rose2 = C(0xfffdbbcf);
		public static readonly Color Neutral8 = C(0xff121212);
		public static readonly Color Neutral7 = C(0xde000000);
		public static readonly Color Neutral6 = C(0x99000000);
		public static readonly Color Neutral5 = C(0x61000000);
		public static readonly Color Neutral4 = C(0x1f000000);
		public static readonly Color Neutral1 = C(0xbdffffff);
		public static readonly Color Neutral0 = C(0xffffffff);
		public static readonly Color FunctionalRed = C(0xffd00036);
		public static readonly Color FunctionalGreen = C(0xff52c41a);
		public static readonly Color FunctionalGrey = C(0xfff6f6f6);

		// ── Theme.kt LightColorPalette roles ──
		public static readonly Color Brand = Shadow5;
		public static readonly Color BrandSecondary = Ocean3;
		public static readonly Color UiBackground = Neutral0;
		public static readonly Color UiBorder = Neutral4;
		public static readonly Color UiFloated = FunctionalGrey;
		public static readonly Color TextPrimary = Shadow5;          // defaults to brand in the gold
		public static readonly Color TextSecondary = Neutral7;
		public static readonly Color TextHelp = Neutral6;
		public static readonly Color TextInteractive = Neutral0;
		public static readonly Color IconPrimary = Shadow5;
		public static readonly Color IconSecondary = Neutral7;
		public static readonly Color IconInteractive = Neutral0;
		public static readonly Color IconInteractiveInactive = Neutral1;
		public static readonly Color TextLink = Ocean11;
		public static readonly Color Error = FunctionalRed;

		// ── Gradient stop lists (light) — Theme.kt:42-49 ──
		public static readonly Color[] Gradient6_1 = { Shadow4, Ocean3, Shadow2, Ocean3, Shadow4 };
		public static readonly Color[] Gradient6_2 = { Rose4, Lavender3, Rose2, Lavender3, Rose4 };
		public static readonly Color[] Gradient3_1 = { Shadow2, Ocean3, Shadow4 };
		public static readonly Color[] Gradient3_2 = { Rose2, Lavender3, Rose4 };
		public static readonly Color[] Gradient2_1 = { Shadow4, Shadow11 };
		public static readonly Color[] Gradient2_2 = { Ocean3, Shadow3 };
		public static readonly Color[] Gradient2_3 = { Lavender3, Rose2 };
		public static readonly Color[] Tornado1 = { Shadow4, Ocean3 };
	}
}
