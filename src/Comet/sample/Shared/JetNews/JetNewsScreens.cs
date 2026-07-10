#nullable enable
using System.Collections.Generic;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.JetNews.JetNewsTheme;

namespace CometSamples.JetNews
{
	/// <summary>
	/// JetNews screens, values-from-source (file:line cites into the gold Kotlin app).
	/// Home = HomeScreens.kt PostList (:423-443) — top section, simple section, popular
	/// carousel, history section, dividers; cards = PostCards.kt / PostCardTop.kt /
	/// PostCardYourNetwork.kt.
	/// </summary>
	public static class JetNewsScreens
	{
		// ── Bookmarks (gold favorites: Set<String> in HomeViewModel) ──
		static readonly HashSet<string> Favorites = new();
		static readonly List<ListView<HomeRow>> FeedLists = new();
		static void ToggleFavorite(string id)
		{
			if (!Favorites.Add(id))
				Favorites.Remove(id);
			foreach (var list in FeedLists)
				list.ReloadData();
		}

		public static void OpenPost(string id) => JetNewsRoot.OpenPost(id);

		// Gold typography (Type.kt defaultTextStyle): Montserrat for EVERY style — already
		// registered in both probes (Jetchat ships the same family).
		static Text Tx(string s) => new Text(s).FontFamily("Montserrat");

		// ── Home feed rows: the gold's LazyColumn sections flattened into one list
		// (PostList :423-443 — top / simple×3+divider / popular / history). ──
		abstract record HomeRow;
		sealed record TopSectionRow(Post Post) : HomeRow;
		sealed record SimpleRow(Post Post) : HomeRow;
		sealed record PopularSectionRow(IReadOnlyList<Post> Posts) : HomeRow;
		sealed record HistoryRow(Post Post) : HomeRow;
		sealed record DividerRow : HomeRow;

		static IReadOnlyList<HomeRow> HomeRows()
		{
			var feed = JetNewsData.Posts;
			var rows = new List<HomeRow> { new TopSectionRow(feed.HighlightedPost), new DividerRow() };
			foreach (var p in feed.RecommendedPosts)
			{
				rows.Add(new SimpleRow(p));
				rows.Add(new DividerRow());
			}
			rows.Add(new PopularSectionRow(feed.PopularPosts));
			rows.Add(new DividerRow());
			foreach (var p in feed.RecentPosts)
			{
				rows.Add(new HistoryRow(p));
				rows.Add(new DividerRow());
			}
			return rows;
		}

		/// <summary>The compact Home screen: center-aligned top app bar + the feed.
		/// <paramref name="topInset"/> clears the status bar (no NavigationSuite here —
		/// JetNews chrome is drawer-first; the drawer lands in the next increment).</summary>
		static ListView<HomeRow> FeedList()
		{
			var list = new ListView<HomeRow>(HomeRows)
			{
				ViewFor = r => r switch
				{
					TopSectionRow t => TopSection(t.Post),
					SimpleRow s => PostCardSimple(s.Post),
					PopularSectionRow p => PopularSection(p.Posts),
					HistoryRow h => PostCardHistory(h.Post),
					_ => Divider(),
				},
			};
			FeedLists.Add(list);
			return list;
		}

		public static View Home(double topInset, System.Action? openDrawer = null)
		{
			JetNewsIcons.Register();

			return new VStack(spacing: 0f)
			{
				new HStack().Frame(height: (float)topInset).FlexShrink(0),
				HomeTopAppBar(openDrawer).FlexShrink(0),
				FeedList().FlexGrow(1).FlexBasis(0),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.Background);
		}

		/// <summary>The expanded home LIST pane (HomeFeedWithArticleDetailsScreen): a
		/// "Search posts" field (inert in the gold too) above the same feed — no app bar.</summary>
		public static View ExpandedListPane()
		{
			JetNewsIcons.Register();

			var field = new HStack(spacing: 0f)
			{
				new Icon("search").IconSize(24).Color(T.OnSurfaceVariant)
					.Frame(width: 48, height: 48).Padding(new Thickness(12))
					.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
				Tx("Search posts").FontSize(16).Color(T.OnSurfaceVariant)
					.VerticalLayoutAlignment(LayoutAlignment.Center),
			}
			.Frame(height: 56)
			.Border(1, T.Outline).CornerRadius(4)
			.Margin(new Thickness(16, 8, 16, 8))
			.HorizontalLayoutAlignment(LayoutAlignment.Fill);

			return new VStack(spacing: 0f)
			{
				field.FlexShrink(0),
				FeedList().FlexGrow(1).FlexBasis(0),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.Background);
		}

		// Gold HomeTopAppBar: CenterAlignedTopAppBar — brand icon nav slot, centered
		// lowercase wordmark, search action (HomeScreens.kt bottom). The gold wordmark is a
		// vector drawable; a styled Text stands in until the asset lands (backlog).
		static View HomeTopAppBar(System.Action? openDrawer) => new HStack(spacing: 0f)
		{
			new Icon("jetnews_logo").IconSize(24).Color(T.Primary)
				.Frame(width: 48, height: 48).Padding(new Thickness(12))
				.FlexShrink(0)
				.OnTap(_ => openDrawer?.Invoke()),
			new HStack().FlexGrow(1),
			Tx("jetnews").FontSize(24)
				.FontWeight(FontWeight.Medium).Color(T.Primary)
				.VerticalLayoutAlignment(LayoutAlignment.Center),
			new HStack().FlexGrow(1),
			new Icon("search").IconSize(24).Color(T.OnSurfaceVariant)
				.Frame(width: 48, height: 48).Padding(new Thickness(12))
				.FlexShrink(0),
		}.Frame(height: 64);

		// ── Top section (HomeScreens.kt:468-479 + PostCardTop.kt): section title
		// titleMedium pad s16/t16/e16; card column pad 16 — image minH 180 rounded,
		// spacer 16, title titleLarge padB 8, author labelLarge padB 4,
		// date-readtime bodySmall onSurfaceVariant. ──
		static View TopSection(Post post) => new VStack(spacing: 0f)
		{
			Tx("Top stories for you").FontSize(16).FontWeight(FontWeight.Medium).Color(T.OnSurface)
				.Padding(new Thickness(16, 16, 16, 0)),
			new VStack(spacing: 0f)
			{
				new Image(post.ImageId).Frame(height: 180).CornerRadius(8)
					.HorizontalLayoutAlignment(LayoutAlignment.Fill),
				new HStack().Frame(height: 16),
				Tx(post.Title).FontSize(22).Color(T.OnSurface)
					.LineBreakMode(LineBreakMode.WordWrap).LineBreak(TextLineBreak.Heading)
					.Padding(new Thickness(0, 0, 0, 8)),
				Tx(post.Metadata.Author.Name).FontSize(14).FontWeight(FontWeight.Medium).Color(T.OnSurface)
					.Padding(new Thickness(0, 0, 0, 4)),
				Tx($"{post.Metadata.Date} - {post.Metadata.ReadTimeMinutes} min read")
					.FontSize(12).Color(T.OnSurfaceVariant),
			}.Padding(new Thickness(16)),
		}.OnTap(_ => OpenPost(post.Id));

		// ── Simple row (PostCards.kt:~85-128): image 40 pad 16 | column v10 (title
		// titleMedium, author-readtime bodyMedium) | bookmark toggle pad v2/h6. ──
		static View PostCardSimple(Post post) => new HStack(spacing: 0f)
		{
			new Image(post.ImageThumbId).Frame(width: 40, height: 40).CornerRadius(4)
				.Margin(new Thickness(16)).FlexShrink(0),
			new VStack(spacing: 2f)
			{
				Tx(post.Title).FontSize(16).FontWeight(FontWeight.Medium).Color(T.OnSurface)
					.LineBreakMode(LineBreakMode.WordWrap).LineBreak(TextLineBreak.Heading),
				Tx($"{post.Metadata.Author.Name} - {post.Metadata.ReadTimeMinutes} min read")
					.FontSize(14).Color(T.OnSurfaceVariant),
			}.Padding(new Thickness(0, 10, 0, 10)).FlexGrow(1).FlexBasis(0)
			.VerticalLayoutAlignment(LayoutAlignment.Center),
			BookmarkButton(post).FlexShrink(0),
		}.OnTap(_ => OpenPost(post.Id));

		static View BookmarkButton(Post post) =>
			new Icon(Favorites.Contains(post.Id) ? "bookmark" : "bookmark_border")
				.IconSize(24).Color(T.OnSurfaceVariant)
				.Frame(width: 48, height: 48)
				.Margin(new Thickness(6, 2, 6, 2))
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.OnTap(_ => ToggleFavorite(post.Id));

		// ── Popular section (HomeScreens.kt:516-541): title titleLarge pad 16; horizontal
		// card row pad h16 spacing 8 (gold: Row+horizontalScroll; a horizontal ListView is
		// the node-backend equivalent); spacer 16. Card (PostCardYourNetwork.kt:54-97):
		// width 280 shape medium — image h100 fill, column pad 16 (title headlineSmall
		// max2, author bodyMedium max1, date-readtime bodySmall). ──
		static View PopularSection(IReadOnlyList<Post> posts)
		{
			var carousel = new ListView<Post>(() => posts)
			{
				Horizontal = true,
				ViewFor = PostCardPopular,
			};
			return new VStack(spacing: 0f)
			{
				Tx("Popular on Jetnews").FontSize(22).Color(T.OnSurface)
					.Padding(new Thickness(16)),
				carousel.Frame(height: 250).Margin(left: 16)
					.HorizontalLayoutAlignment(LayoutAlignment.Fill),
				new HStack().Frame(height: 16),
			};
		}

		static View PostCardPopular(Post post) => new VStack(spacing: 0f)
		{
			new Image(post.ImageId).Frame(width: 280, height: 100),
			new VStack(spacing: 0f)
			{
				Tx(post.Title).FontSize(24).Color(T.OnSurface)
					.LineBreakMode(LineBreakMode.WordWrap).LineBreak(TextLineBreak.Heading).MaxLines(2),
				Tx(post.Metadata.Author.Name).FontSize(14).Color(T.OnSurface)
					.MaxLines(1).Padding(new Thickness(0, 4, 0, 0)),
				Tx($"{post.Metadata.Date} - {post.Metadata.ReadTimeMinutes} min read")
					.FontSize(12).Color(T.OnSurfaceVariant),
			}.Padding(new Thickness(16)),
		}
		.Frame(width: 280)
		.Background(T.SurfaceContainerLow).CornerRadius(4).Shadow(new Comet.Graphics.Shadow().WithRadius(2))
		.Margin(right: 8)
		.OnTap(_ => OpenPost(post.Id));

		// ── History row (PostCards.kt:131-160): image 40 pad 16 | column v12 (overline
		// labelMedium "BASED ON YOUR HISTORY", title, author-readtime pad t4) | more_vert. ──
		static View PostCardHistory(Post post) => new HStack(spacing: 0f)
		{
			new Image(post.ImageThumbId).Frame(width: 40, height: 40).CornerRadius(4)
				.Margin(new Thickness(16)).FlexShrink(0),
			new VStack(spacing: 2f)
			{
				Tx("BASED ON YOUR HISTORY").FontSize(12).FontWeight(FontWeight.Medium)
					.Color(T.OnSurfaceVariant),
				Tx(post.Title).FontSize(16).FontWeight(FontWeight.Medium).Color(T.OnSurface)
					.LineBreakMode(LineBreakMode.WordWrap).LineBreak(TextLineBreak.Heading),
				Tx($"{post.Metadata.Author.Name} - {post.Metadata.ReadTimeMinutes} min read")
					.FontSize(14).Color(T.OnSurfaceVariant).Padding(new Thickness(0, 4, 0, 0)),
			}.Padding(new Thickness(0, 12, 0, 12)).FlexGrow(1).FlexBasis(0),
			new Icon("more_vert").IconSize(24).Color(T.OnSurfaceVariant)
				.Frame(width: 48, height: 48).Margin(new Thickness(0, 12, 4, 0)).FlexShrink(0),
		}.OnTap(_ => OpenPost(post.Id));

		// Gold PostListDivider: 1dp, pad h14, onSurface 12% (HomeScreens.kt tail).
		static View Divider() => new HStack()
			.Frame(height: 1)
			.Margin(left: 14, right: 14)
			.Background(T.Divider)
			.HorizontalLayoutAlignment(LayoutAlignment.Fill);
	}
}
