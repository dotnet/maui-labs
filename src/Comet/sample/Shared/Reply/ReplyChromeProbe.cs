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
	/// M1 scaffold: Reply's adaptive nav chrome on the <see cref="NavigationSuite"/> primitive —
	/// bottom bar &lt;600dp, rail (menu + FAB header) 600–1199dp, permanent drawer ≥1200dp,
	/// all real M3 widgets, the swap owned by the suite's node (window-metrics reactive).
	/// Placeholder route content per gold destination; the inbox/detail screens replace it as
	/// the sample builds out.
	/// </summary>
	public class ReplyProbeRoot : View
	{
		static readonly Signal<int> Selected = new(0);
		static readonly string[] Titles = { "Inbox", "Articles", "Direct Messages", "Groups" };

		[Body]
		View body()
		{
			ReplyIcons.Register();

			var items = new[]
			{
				NavItem("inbox", "Inbox"),
				NavItem("article", "Articles"),
				NavItem("chat_outline", "Direct Messages"),
				NavItem("people_outline", "Groups"),
			};

			// Placeholder route content (EmptyComingSoon-shaped); bound so a selection
			// re-renders just the texts. FlexGrow spacers center the block vertically
			// (alignment modifiers position a view in its parent, not its children).
			var content = new VStack(spacing: 8)
			{
				new HStack().FlexGrow(1),
				new Text(() => Titles[Selected.Value])
					.FontSize(22).FontWeight(FontWeight.Bold)
					.Color(ReplyTheme.Primary)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
				new Text("This screen is still under construction.")
					.FontSize(12).Color(ReplyTheme.Outline)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
				// TEMP diagnostic: live window size (remove before the sample lands)
				new Text(() => $"win {CometWindowMetrics.Shared.SizeDp.Value.Width:0}x{CometWindowMetrics.Shared.SizeDp.Value.Height:0} {CometWindowMetrics.Shared.WidthClass}")
					.FontSize(11).Color(ReplyTheme.Outline)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
				new HStack().FlexGrow(1),
			}
			.Background(ReplyTheme.Background);

			return new NavigationSuite(Selected, items, content,
				railHeader: RailHeader(), drawerHeader: DrawerHeader());
		}

		// The gold drawer variant shows labeled items — label views feed NavigationDrawerItem
		// slots; the bar/rail render icon-only, matching the gold.
		static NavigationItem NavItem(string icon, string label) =>
			new(new Icon(icon).IconSize(24), new Text(label).FontSize(14));

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

		// The gold permanent-drawer header: "REPLY" wordmark + the Compose extended FAB.
		static View DrawerHeader() => new VStack(spacing: 8)
		{
			new Text("REPLY").FontSize(16).FontWeight(FontWeight.Semibold)
				.Color(ReplyTheme.Primary),
			new HStack(spacing: 8)
			{
				new Icon("edit").IconSize(18).Color(ReplyTheme.OnTertiaryContainer),
				new Text("Compose").FontSize(14).Color(ReplyTheme.OnTertiaryContainer),
			}
			.Frame(height: 56)
			.Background(ReplyTheme.TertiaryContainer)
			.CornerRadius(16)
			.Padding(new Thickness(16, 0)),
		}.Padding(new Thickness(16, 16, 16, 40));
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
