#nullable enable
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;

namespace CometSamples.Reply
{
	/// <summary>
	/// M1 scaffold: Reply's adaptive nav chrome driving the REAL M3 widgets — bottom
	/// NavigationBar under 600dp, NavigationRail (menu + FAB header) at 600dp+ — switched
	/// reactively off <see cref="CometWindowMetrics"/>, with the four gold destinations and
	/// a placeholder content pane per route. The inbox/detail screens replace the
	/// placeholders as the sample builds out; this composition hardens into the
	/// NavigationSuite primitive (docs/adaptive-primitives-design.md).
	/// </summary>
	public class ReplyProbeRoot : View
	{
		static readonly Signal<int> Selected = new(0);
		static readonly string[] Titles = { "Inbox", "Articles", "Direct Messages", "Groups" };

		[Body]
		View body()
		{
			ReplyIcons.Register();
			var metrics = this.GetWindowMetrics();
			bool useRail = metrics.WidthClass != WindowWidthClass.Compact;

			var items = new[]
			{
				new NavigationItem(new Icon("inbox").IconSize(24)),
				new NavigationItem(new Icon("article").IconSize(24)),
				new NavigationItem(new Icon("chat_outline").IconSize(24)),
				new NavigationItem(new Icon("people_outline").IconSize(24)),
			};

			// Placeholder route content (EmptyComingSoon-shaped); bound so a selection
			// re-renders just the texts.
			var content = new VStack(spacing: 8)
			{
				new Text(() => Titles[Selected.Value])
					.FontSize(22).FontWeight(FontWeight.Bold)
					.Color(ReplyTheme.Primary),
				new Text("This screen is still under construction.")
					.FontSize(12).Color(ReplyTheme.Outline),
				// TEMP diagnostic: live window size (remove before the sample lands)
				new Text(() => $"win {CometWindowMetrics.Shared.SizeDp.Value.Width:0}x{CometWindowMetrics.Shared.SizeDp.Value.Height:0} {CometWindowMetrics.Shared.WidthClass}")
					.FontSize(11).Color(ReplyTheme.Outline),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Center)
			.VerticalLayoutAlignment(LayoutAlignment.Center);

			return useRail
				? new HStack
				{
					new NavigationRail(Selected, items, header: RailHeader()).FlexShrink(0),
					content.FlexGrow(1).FlexBasis(0),
				}.Background(ReplyTheme.Background)
				: new VStack
				{
					content.FlexGrow(1).FlexBasis(0),
					new NavigationBar(Selected, items).FlexShrink(0),
				}.Background(ReplyTheme.Background);
		}

		// The gold rail header: a menu affordance above the compose FAB (drawer wiring lands
		// with the modal-drawer increment).
		static View RailHeader() => new VStack(spacing: 4)
		{
			new Icon("menu").IconSize(24).Color(ReplyTheme.OnSurfaceVariant),
			new Icon("edit").IconSize(18).Color(ReplyTheme.OnTertiaryContainer)
				.Frame(width: 56, height: 56)
				.Background(ReplyTheme.TertiaryContainer)
				.CornerRadius(16),
		}.HorizontalLayoutAlignment(LayoutAlignment.Center);
	}

	/// <summary>Reply's static light scheme — literals from the gold's ui/theme/Color.kt
	/// (dynamicColor=false; deterministic, no seed generation). Grows role-by-role as the
	/// sample needs them; the full table ports with the theme increment.</summary>
	public static class ReplyTheme
	{
		public static readonly Color Primary = Color.FromArgb("#805610");
		public static readonly Color Background = Color.FromArgb("#FFF8F4");
		public static readonly Color Outline = Color.FromArgb("#817567");
		public static readonly Color OnSurfaceVariant = Color.FromArgb("#4F4539");
		public static readonly Color TertiaryContainer = Color.FromArgb("#D4EABB");
		public static readonly Color OnTertiaryContainer = Color.FromArgb("#102004");
	}
}
