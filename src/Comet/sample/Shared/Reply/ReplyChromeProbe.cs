#nullable enable
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Reply.ReplyTheme;

namespace CometSamples.Reply
{
	/// <summary>
	/// Reply's root: the adaptive NavigationSuite hosting a ContentSwitcher over the four
	/// gold routes (Inbox = real ListDetail screens; Articles / DirectMessages / Groups =
	/// EmptyComingSoon, matching ReplyApp.kt:128-153).
	/// </summary>
	public class ReplyProbeRoot : View
	{
		static readonly Signal<int> Selected = new(0);

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

			var routes = new ContentSwitcher(Selected, new[]
			{
				ReplyScreens.Inbox(),
				ReplyScreens.ComingSoon(),
				ReplyScreens.ComingSoon(),
				ReplyScreens.ComingSoon(),
			});

			return new NavigationSuite(Selected, items, routes,
					railHeader: RailHeader(), drawerHeader: DrawerHeader())
				.Background(T.Background);   // paints the safe-area strips too
		}

		// The gold drawer variant shows labeled items — label views feed NavigationDrawerItem
		// slots; the bar/rail render icon-only, matching the gold.
		static NavigationItem NavItem(string icon, string label) =>
			new(new Icon(icon).IconSize(24), new Text(label).FontSize(14));

		// The gold rail header: a menu affordance above the compose FAB (drawer wiring lands
		// with the modal-drawer increment).
		static View RailHeader() => new VStack(spacing: 4)
		{
			new Icon("menu").IconSize(24).Color(T.OnSurfaceVariant),
			new Icon("edit").IconSize(18).Color(T.OnTertiaryContainer)
				.Frame(width: 56, height: 56)
				.Background(T.TertiaryContainer)
				.CornerRadius(16),
		}.HorizontalLayoutAlignment(LayoutAlignment.Center);

		// The gold permanent-drawer header: "REPLY" wordmark + the Compose extended FAB.
		static View DrawerHeader() => new VStack(spacing: 8)
		{
			new Text("REPLY").FontSize(16).FontWeight(FontWeight.Semibold)
				.Color(T.Primary),
			new HStack(spacing: 8)
			{
				new Icon("edit").IconSize(18).Color(T.OnTertiaryContainer),
				new Text("Compose").FontSize(14).Color(T.OnTertiaryContainer),
			}
			.Frame(height: 56)
			.Background(T.TertiaryContainer)
			.CornerRadius(16)
			.Padding(new Thickness(16, 0)),
		}.Padding(new Thickness(16, 16, 16, 40));
	}
}
