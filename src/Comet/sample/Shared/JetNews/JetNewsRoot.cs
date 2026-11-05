#nullable enable
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;
using T = CometSamples.JetNews.JetNewsTheme;

namespace CometSamples.JetNews
{
	/// <summary>
	/// JetNews chrome + routing over the adaptive primitives (the gold JetnewsApp):
	/// NavigationSuite with the JetNews breakpoint policy (chromeless + modal drawer
	/// below 840dp, REAL NavigationRail with selected-only labels above — JetnewsApp.kt's
	/// isExpandedScreen), destinations = Home / Interests (real NavigationDrawerItems in
	/// the sheet), and Home = ListDetail (compact: full-screen article with system-back;
	/// expanded: feed | detail at the gold's ~⅓ split with a "Select a post" placeholder).
	/// </summary>
	public class JetNewsRoot : View
	{
		/// <summary>0 = Home, 1 = Interests (the drawer/rail destinations).</summary>
		public static readonly Signal<int> SelectedDest = new(0);
		public static readonly Signal<bool> ArticleOpen = new(false);
		public static readonly Signal<Post> CurrentPost = new(JetNewsData.Posts.HighlightedPost);
		public static readonly Signal<bool> DrawerOpen = new(false);

		// Backend-written active chrome variant → derived indices for the variant-aware
		// slots (compact app bar vs expanded search field; placeholder vs article).
		static readonly Signal<int> Variant = new((int)NavigationSuiteVariant.None);
		static readonly Signal<int> HomeChrome = new(0);
		static readonly Signal<int> DetailContent = new(0);

		static JetNewsRoot()
		{
			Variant.PropertyChanged += (_, _) =>
				HomeChrome.Value = Variant.Peek() == (int)NavigationSuiteVariant.Rail ? 1 : 0;
			ArticleOpen.PropertyChanged += (_, _) =>
				DetailContent.Value = ArticleOpen.Peek() ? 1 : 0;
			// Selecting a destination dismisses an open article (the gold's PopUpTo reset) —
			// without this, drawer → Home while an article is open lands on the stale article.
			SelectedDest.PropertyChanged += (_, _) => ArticleOpen.Value = false;
		}

		public static void OpenPost(string id)
		{
			if (JetNewsData.Posts.AllPosts.FirstOrDefault(p => p.Id == id) is { } post)
			{
				CurrentPost.Value = post;
				ArticleOpen.Value = true;
			}
		}

		public JetNewsRoot() { }

		static Text Tx(string s) => new Text(s).FontFamily("Montserrat");

		[Body]
		View body()
		{
			JetNewsIcons.Register();
			JetNewsScreens.ResetFeedLists();   // a rebuild replaces the live lists — drop the old generation

			// Home pane: compact = wordmark app bar; expanded = search field (gold
			// HomeFeedWithArticleDetailsScreen swaps chrome the same way).
			var homeList = new ContentSwitcher(HomeChrome, new View[]
			{
				JetNewsScreens.Home(topInset: 0, openDrawer: () => DrawerOpen.Value = true),
				JetNewsScreens.ExpandedListPane(),
			});

			var detail = new ContentSwitcher(DetailContent, new View[]
			{
				SelectAPost(),
				JetNewsArticle.Screen(CurrentPost, topInset: 0, onBack: () => ArticleOpen.Value = false),
			});

			var home = new ListDetail(ArticleOpen, homeList, detail, listFraction: 1 / 3.0);

			var routes = new ContentSwitcher(SelectedDest, new View[]
			{
				home,
				JetNewsInterests.Screen(topInset: 0, openDrawer: () => DrawerOpen.Value = true),
			});

			var items = new[]
			{
				// onSelect fires even when the index is unchanged — re-tapping Home
				// still dismisses an open article (the equality-gated signal wouldn't).
				new NavigationItem(new Icon("home").IconSize(24), Tx("Home").FontSize(14),
					onSelect: () => ArticleOpen.Value = false),
				new NavigationItem(new Icon("list_alt").IconSize(24), Tx("Interests").FontSize(14)),
			};

			return new NavigationSuite(SelectedDest, items, routes,
					railHeader: RailHeader(), drawerHeader: DrawerHeader(),
					drawerOpen: DrawerOpen,
					// M3 tokens for the hand-composed iOS twin (Android's real widgets self-theme).
					containerColor: T.Background, indicatorColor: T.SecondaryContainer,
					// JetnewsApp.kt: expanded (≥840dp) = rail; below = drawer-only chrome.
					variantFor: (w, _) => w >= 840 ? NavigationSuiteVariant.Rail : NavigationSuiteVariant.None,
					railShowsSelectedLabel: true,
					variantSignal: Variant)
				.Background(T.Background);
		}

		// AppNavRail.kt header: the jetnews logo vector tinted primary, 12dp vertical padding.
		static View RailHeader() =>
			new Icon("jetnews_logo").IconSize(24).Color(T.Primary)
				.Frame(width: 80, height: 48).Padding(new Thickness(28, 12, 28, 12));

		// AppDrawer.kt JetNewsLogo row: logo (primary) + wordmark (onSurfaceVariant), 8dp gap.
		static View DrawerHeader() => new HStack(spacing: 0f)
		{
			new Icon("jetnews_logo").IconSize(24).Color(T.Primary).FlexShrink(0),
			new HStack().Frame(width: 8),
			new Icon("jetnews_wordmark").Color(T.OnSurfaceVariant).IconFillFrame()
				.Frame(width: 80, height: 24)
				.VerticalLayoutAlignment(LayoutAlignment.Center),
		}.Padding(new Thickness(28, 24, 28, 24));

		// HomeScreens.kt: expanded detail placeholder — centered "Select a post".
		static View SelectAPost() => new VStack(spacing: 0f)
		{
			new HStack().FlexGrow(1),
			Tx("Select a post").FontSize(16).FontWeight(FontWeight.Medium).Color(T.OnSurface)
				.HorizontalLayoutAlignment(LayoutAlignment.Center),
			new HStack().FlexGrow(1),
		}
		.HorizontalLayoutAlignment(LayoutAlignment.Fill)
		.VerticalLayoutAlignment(LayoutAlignment.Fill)
		.Background(T.Background);
	}
}
