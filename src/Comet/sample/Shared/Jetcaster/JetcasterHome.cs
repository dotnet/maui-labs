#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Jetcaster.JetcasterTheme;

namespace CometSamples.Jetcaster
{
	/// <summary>
	/// Jetcaster Home (mobile ui/home/Home.kt), values-from-source: dark scheme over a
	/// radial primary scrim, decorative M3 SearchBar top bar, ONE adaptive grid
	/// (Adaptive(362dp)) switching Library/Discover on the floating Library|Discover
	/// pill. Discover = category FilterChips + top-podcasts carousel + episode cards;
	/// Library = followed carousel + Latest Episodes. The pill toolbar is hand-composed
	/// until HorizontalFloatingToolbar binds (backlog); podcast-details pane and player
	/// are the next increments.
	/// </summary>
	public class JetcasterRoot : View
	{
		/// <summary>false = Discover (the gold's default with no followed podcasts).</summary>
		public static readonly Signal<bool> ShowLibrary = new(false);
		public static readonly Signal<string> SelectedCategory = new(string.Empty);

		static ListView<object>? _grid;
		static ListView<int>? _pill;
		static bool _hooked;

		readonly double _topInset;

		public JetcasterRoot(double topInset = 0) => _topInset = topInset;

		[Body]
		View body()
		{
			JetcasterIcons.Register();

			var grid = new ListView<object>(Rows)
			{
				ViewFor = RowView,
				GridAdaptiveMinWidth = 362,
			};
			// One-row list so the pill re-styles when the selection changes (a Peek-built
			// pill freezes — the Jetsnack bottom-bar lesson).
			var pill = new ListView<int>(() => new[] { 0 }) { ViewFor = _ => PillToolbar() };
			_pill = pill;
			_grid = grid;
			if (!_hooked)
			{
				// Subscribe ONCE via the current-instance slot (statics outlive rebuilds).
				_hooked = true;
				ShowLibrary.PropertyChanged += (_, _) => { _grid?.ReloadData(); _pill?.ReloadData(); };
				SelectedCategory.PropertyChanged += (_, _) => _grid?.ReloadData();
				PodcastStore.Version.PropertyChanged += (_, _) => _grid?.ReloadData();
			}

			return new ZStack
			{
				// HomeScreenBackground: colorScheme.background with a radial primary@15%
				// scrim OVER it (two layers — a gradient replaces a solid on one node).
				new VStack(spacing: 0f)
					.HorizontalLayoutAlignment(LayoutAlignment.Fill)
					.VerticalLayoutAlignment(LayoutAlignment.Fill)
					.Background(T.Background),
				new VStack(spacing: 0f)
					.HorizontalLayoutAlignment(LayoutAlignment.Fill)
					.VerticalLayoutAlignment(LayoutAlignment.Fill)
					.BackgroundGradient(new GradientSpec(
						new[] { T.Primary.WithAlpha(0.15f), Colors.Transparent },
						GradientDirection.Radial)),
				new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)_topInset).FlexShrink(0),
					HomeAppBar().FlexShrink(0),
					grid.FlexGrow(1).FlexBasis(0),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
				// The floating Library|Discover pill (gold HorizontalFloatingToolbar,
				// bottom-center over the grid).
				new VStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					pill.Frame(width: 300, height: 72).FlexShrink(0),
					new HStack().Frame(height: 24).FlexShrink(0),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill);
		}

		// ── rows ────────────────────────────────────────────────────────────────────
		abstract record Row;
		sealed record ChipsRow : Row;
		sealed record CarouselRow(IReadOnlyList<PodcastWithEpisodes> Feeds, bool Followed) : Row;
		sealed record HeaderRow(string Title) : Row;
		sealed record EpisodeRow(EpisodeToPodcast Item) : Row;

		static IReadOnlyList<object> Rows()
		{
			var rows = new List<object>();
			if (ShowLibrary.Peek())
			{
				var followed = PodcastStore.FollowedFeeds;
				rows.Add(new CarouselRow(followed, Followed: true));
				rows.Add(new HeaderRow("Latest Episodes"));
				rows.AddRange(PodcastStore.LatestEpisodes(followed).Select(e => (object)new EpisodeRow(e)));
			}
			else
			{
				rows.Add(new ChipsRow());
				var category = CurrentCategory();
				if (category is not null)
				{
					var feeds = PodcastStore.InCategory(category);
					rows.Add(new CarouselRow(feeds, Followed: false));
					rows.AddRange(PodcastStore.LatestEpisodes(feeds).Select(e => (object)new EpisodeRow(e)));
				}
			}
			rows.Add("spacer");
			return rows;
		}

		static Category? CurrentCategory()
		{
			var categories = PodcastStore.Categories;
			if (categories.Count == 0)
				return null;
			var name = SelectedCategory.Peek();
			return categories.FirstOrDefault(c => c.Name == name) ?? categories[0];
		}

		static View RowView(object row) => row switch
		{
			ChipsRow => CategoryChips(),
			CarouselRow c => PodcastCarousel(c.Feeds, c.Followed),
			HeaderRow h => T.TitleLarge(h.Title).Color(T.OnSurface)
				.Padding(new Thickness(16, 16, 16, 8)),
			EpisodeRow e => EpisodeCard(e.Item),
			_ => new HStack().Frame(height: 96),
		};

		// HomeAppBar: the REAL M3 SearchBar, decorative (the gold never expands it) —
		// search icon leading, account icon trailing.
		static View HomeAppBar() => new VStack(spacing: 0f)
		{
			new SearchBar(SearchQuery,
				placeholder: T.BodyLarge("Search for a podcast").Color(T.OnSurfaceVariant),
				content: new VStack(spacing: 0f) { T.BodyMedium("Search lands with a later increment").Color(T.OnSurfaceVariant).Padding(new Thickness(16)) },
				leading: new Icon("search").IconSize(24).Color(T.OnSurfaceVariant),
				trailing: new Icon("account_circle").IconSize(24).Color(T.OnSurfaceVariant),
				containerColor: T.SurfaceContainerHigh),
		}.Padding(new Thickness(16, 8, 16, 8));

		static readonly Signal<string> SearchQuery = new(string.Empty);

		// PillToolbar (gold HorizontalFloatingToolbar + two toggle buttons).
		static View PillToolbar()
		{
			View Half(string icon, string label, bool selected, Action onTap)
			{
				var row = new HStack(spacing: 8f)
				{
					new Icon(icon).IconSize(20)
						.Color(selected ? T.OnSecondary : T.OnSurface)
						.VerticalLayoutAlignment(LayoutAlignment.Center),
					T.LabelLarge(label)
						.Color(selected ? T.OnSecondary : T.OnSurface)
						.VerticalLayoutAlignment(LayoutAlignment.Center),
				}
				.Frame(height: 40)
				.Padding(new Thickness(16, 0, 16, 0))
				.CornerRadius(20)
				.OnTap(_ => onTap());
				return selected ? row.Background(T.Secondary) : row;
			}

			bool library = ShowLibrary.Peek();
			return new HStack(spacing: 4f)
			{
				Half("library_music", "Library", library, () => ShowLibrary.Value = true),
				Half("music_note", "Discover", !library, () => ShowLibrary.Value = false),
			}
			.Frame(height: 56)
			.Padding(new Thickness(8, 8, 8, 8))
			.Background(T.SurfaceContainer.WithAlpha(0.94f))
			.CornerRadius(28);
		}

		// Discover's category tabs: a LazyRow of REAL M3 FilterChips (Discover.kt:96-118).
		static View CategoryChips()
		{
			var row = new ListView<Category>(() => PodcastStore.Categories)
			{
				Horizontal = true,
				ViewFor = category => new VStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					new FilterChip(
						selected: CurrentCategory()?.Name == category.Name,
						onClick: () => SelectedCategory.Value = category.Name,
						label: T.LabelLarge(category.Name).Color(
							CurrentCategory()?.Name == category.Name ? T.OnSecondaryContainer : T.OnSurfaceVariant)),
					new HStack().FlexGrow(1),
				}.Frame(height: 48).Padding(new Thickness(0, 0, 8, 0)),
			};
			return row.Frame(height: 48).Margin(left: 16)
				.HorizontalLayoutAlignment(LayoutAlignment.Fill);
		}

		// Top-podcasts row: the REAL M3 HorizontalUncontainedCarousel
		// (PodcastCategory.kt:127); Library uses the MultiBrowse flavor (Home.kt:659).
		static View PodcastCarousel(IReadOnlyList<PodcastWithEpisodes> feeds, bool followedStyle)
		{
			var row = new ListView<PodcastWithEpisodes>(() => feeds)
			{
				Horizontal = true,
				Carousel = followedStyle ? ListCarousel.MultiBrowse : ListCarousel.Uncontained,
				CarouselItemWidth = followedStyle ? 220 : 140,
				ViewFor = feed => CarouselItem(feed, followedStyle),
			};
			return row.Frame(height: followedStyle ? 220 : 200).Margin(left: 16)
				.HorizontalLayoutAlignment(LayoutAlignment.Fill);
		}

		// TopPodcastRowItem / FollowedPodcastCarouselItem: artwork with a bottom
		// Transparent→Black scrim, follow (+/✓) chip top-leading, title on the scrim.
		static View CarouselItem(PodcastWithEpisodes feed, bool followedStyle)
		{
			double size = followedStyle ? 200 : 124;
			bool followed = PodcastStore.IsFollowed(feed.Podcast.Uri);
			return new VStack(spacing: 0f)
			{
				new ZStack
				{
					new Image(feed.Podcast.ImageUrl ?? "").Frame(width: (float)size, height: (float)size)
						.CornerRadius(16),
					new VStack(spacing: 0f) { new HStack().Frame(height: (float)(size / 2)) }
						.Frame(width: (float)size, height: (float)size)
						.BackgroundGradient(new GradientSpec(
							new[] { Colors.Transparent, Colors.Black },
							GradientDirection.Vertical))
						.CornerRadius(16)
						.VerticalLayoutAlignment(LayoutAlignment.Fill),
					new Icon(followed ? "check" : "add").IconSize(20)
						.Color(T.OnSecondary)
						.Frame(width: 36, height: 36).Padding(new Thickness(8))
						.Background(T.Secondary).CornerRadius(10)
						.Margin(new Thickness(8, 8, 0, 0))
						.HorizontalLayoutAlignment(LayoutAlignment.Start)
						.VerticalLayoutAlignment(LayoutAlignment.Start)
						.OnTap(_ => PodcastStore.ToggleFollowed(feed.Podcast.Uri)),
					T.TitleSmall(feed.Podcast.Title).Color(Colors.White).MaxLines(2)
						.Padding(new Thickness(10, 0, 10, 10))
						.HorizontalLayoutAlignment(LayoutAlignment.Start)
						.VerticalLayoutAlignment(LayoutAlignment.End),
				}.Frame(width: (float)size, height: (float)size),
			}.Padding(new Thickness(0, 0, 8, 0));
		}

		// EpisodeListItem: Surface card — texts + artwork, then the play row
		// (SwipeToDismiss + shared elements are later increments).
		static View EpisodeCard(EpisodeToPodcast item) => new VStack(spacing: 0f)
		{
			new VStack(spacing: 0f)
			{
				new HStack(spacing: 0f)
				{
					new VStack(spacing: 4f)
					{
						T.TitleMedium(item.Episode.Title).Color(T.OnSurface).MaxLines(2),
						T.BodyMedium(item.Podcast.Title).Color(T.OnSurfaceVariant).MaxLines(1),
					}.FlexGrow(1).FlexBasis(0),
					new Image(item.Podcast.ImageUrl ?? "").Frame(width: 56, height: 56)
						.CornerRadius(8)
						.Margin(new Thickness(16, 0, 0, 0)).FlexShrink(0),
				}.HorizontalLayoutAlignment(LayoutAlignment.Fill),
				new HStack().Frame(height: 12),
				new HStack(spacing: 0f)
				{
					new Icon("play_arrow").IconSize(24).Color(T.OnPrimary)
						.Frame(width: 40, height: 40).Padding(new Thickness(8))
						.Background(T.Primary).CornerRadius(20)
						.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0)
						.OnTap(_ =>
						{
							MockEpisodePlayer.SetCurrent(item.Episode.Uri);
							MockEpisodePlayer.Play();
						}),
					T.BodySmall(EpisodeMeta(item.Episode)).Color(T.OnSurfaceVariant)
						.Padding(new Thickness(12, 0, 0, 0))
						.VerticalLayoutAlignment(LayoutAlignment.Center)
						.FlexGrow(1).FlexBasis(0),
					// REAL M3 IconButtons — the gold's EpisodeListItem uses IconButton for
					// queue-add and overflow (the play button is the gold's own hand-roll).
					new IconButton(() => MockEpisodePlayer.AddToQueue(item.Episode.Uri),
							new Icon("playlist_add").IconSize(22).Color(T.OnSurfaceVariant))
						.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
					new IconButton(() => { /* the gold's overflow is a TODO too */ },
							new Icon("more_vert").IconSize(22).Color(T.OnSurfaceVariant))
						.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
				}.HorizontalLayoutAlignment(LayoutAlignment.Fill),
			}
			.Padding(new Thickness(16))
			.Background(T.SurfaceContainer.WithAlpha(0.62f))
			.CornerRadius(16)
			.HorizontalLayoutAlignment(LayoutAlignment.Fill),
		}.Padding(new Thickness(16, 6, 16, 6));

		static string EpisodeMeta(Episode e)
		{
			string date = e.Published == default ? "" : e.Published.ToString("MMM d, yyyy");
			string dur = e.Duration is { } d ? $"{(int)Math.Round(d.TotalMinutes)} mins" : "";
			return dur.Length > 0 ? $"{date} • {dur}" : date;
		}
	}

	/// <summary>Jetcaster's Material Icons glyph map (the shared one-font approach —
	/// same registration path as the other samples).</summary>
	static class JetcasterIcons
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

			Add("search", 0xE8B6);          // app-bar search
			Add("account_circle", 0xE853);  // app-bar trailing
			Add("library_music", 0xE030);   // pill toolbar
			Add("music_note", 0xE405);
			Add("add", 0xE145);             // follow
			Add("check", 0xE5CA);           // followed
			Add("play_arrow", 0xE037);      // episode play
			Add("playlist_add", 0xE03B);    // add to queue
			Add("more_vert", 0xE5D4);       // episode overflow

			IconFont.Register(Font, map);
		}
	}
}
