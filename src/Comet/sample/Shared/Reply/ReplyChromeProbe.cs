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
		static readonly Signal<bool> DetailOpen = new(false);
		static readonly Signal<int> OpenedEmail = new(0);
		static readonly string[] Titles = { "Inbox", "Articles", "Direct Messages", "Groups" };
		static readonly (string sender, string subject)[] Emails =
		{
			("Google", "Package shipped!"),
			("Ali", "Brunch this weekend?"),
			("Allison", "Bonjour from Paris"),
			("Kim", "High school reunion?"),
		};

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

			return new NavigationSuite(Selected, items, new ReplyRouteContent(),
					railHeader: RailHeader(), drawerHeader: DrawerHeader())
				.Background(ReplyTheme.Background);   // paints the safe-area strips too
		}

		/// <summary>Route content: the ListDetail scaffold with placeholder inbox rows and a
		/// bound detail pane. Route switching (coming-soon screens) stays bound-text-only for
		/// now; real per-route content lands with the sample screens.</summary>
		sealed class ReplyRouteContent : View
		{
			[Body]
			View body() => new ListDetail(DetailOpen, InboxList(), EmailDetail());
		}

		// Placeholder inbox list: title + tappable rows that open the detail.
		static View InboxList()
		{
			var stack = new VStack(spacing: 8) { };
			stack.Add(new Text(() => Titles[Selected.Value])
				.FontSize(22).FontWeight(FontWeight.Bold)
				.Color(ReplyTheme.Primary)
				.Padding(new Thickness(16, 16, 16, 8)));
			for (int i = 0; i < Emails.Length; i++)
			{
				int index = i;
				stack.Add(new VStack(spacing: 2)
				{
					new Text(Emails[i].sender).FontSize(12).Color(ReplyTheme.Outline),
					new Text(Emails[i].subject).FontSize(16).Color(ReplyTheme.OnSurfaceVariant),
				}
				.Padding(new Thickness(20, 12, 20, 12))
				.Background(ReplyTheme.SurfaceVariant)
				.CornerRadius(16)
				.Margin(left: 16, right: 16)
				.OnTap(_ =>
				{
					OpenedEmail.Value = index;
					DetailOpen.Value = true;
				}));
			}
			// TEMP diagnostic: live window size (remove before the sample lands)
			stack.Add(new Text(() => $"win {CometWindowMetrics.Shared.SizeDp.Value.Width:0}x{CometWindowMetrics.Shared.SizeDp.Value.Height:0} {CometWindowMetrics.Shared.WidthClass}")
				.FontSize(11).Color(ReplyTheme.Outline)
				.Padding(new Thickness(16, 8, 16, 0)));
			return stack.Background(ReplyTheme.Background);
		}

		// Placeholder detail pane: bound to the opened email.
		static View EmailDetail() => new VStack(spacing: 8)
		{
			new Text(() => Emails[OpenedEmail.Value].subject)
				.FontSize(20).FontWeight(FontWeight.Semibold)
				.Color(ReplyTheme.OnSurfaceVariant)
				.Padding(new Thickness(20, 16, 20, 4)),
			new Text(() => $"From {Emails[OpenedEmail.Value].sender}")
				.FontSize(13).Color(ReplyTheme.Outline)
				.Padding(new Thickness(20, 0, 20, 0)),
			new Text("Detail pane placeholder — the thread items land with the sample screens.")
				.FontSize(14).Color(ReplyTheme.Outline)
				.Padding(new Thickness(20, 16, 20, 0)),
		}.Background(ReplyTheme.InverseOnSurface);

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

}
