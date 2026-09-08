#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CometSamples.Jetcaster
{
	// Verbatim port of the gold entities (core/data/database/model) — same names,
	// same shape. Room is the gold's offline cache, not observable behavior, so the
	// stores here are in-memory (docs/jetcaster-parity-backlog.md §Data).

	public sealed record Podcast(
		string Uri,
		string Title,
		string? Description = null,
		string? Author = null,
		string? ImageUrl = null,
		string? Copyright = null);

	public sealed record Episode(
		string Uri,
		string PodcastUri,
		string Title,
		string? Subtitle = null,
		string? Summary = null,
		string? Author = null,
		DateTimeOffset Published = default,
		TimeSpan? Duration = null);

	public sealed record Category(string Name);

	/// <summary>The gold's EpisodeToPodcast join.</summary>
	public sealed record EpisodeToPodcast(Episode Episode, Podcast Podcast);

	/// <summary>One parsed feed: the podcast + its episodes + its categories.</summary>
	public sealed record PodcastWithEpisodes(
		Podcast Podcast,
		IReadOnlyList<Episode> Episodes,
		IReadOnlyList<Category> Categories);

	/// <summary>
	/// In-memory twin of the gold's Podcast/Episode/Category stores, filled by
	/// <see cref="JetcasterFeedParser"/> from the bundled fixture feeds (fixture mode)
	/// or live fetches (later increment). Follow state is a signal-backed set like the
	/// gold's PodcastFollowedEntry table.
	/// </summary>
	public static class PodcastStore
	{
		static readonly List<PodcastWithEpisodes> Feeds = new();
		static readonly HashSet<string> Followed = new(StringComparer.Ordinal);

		/// <summary>Bumped on any store change — screens subscribe once and reload.</summary>
		public static readonly Comet.Reactive.Signal<int> Version = new(0);

		public static void Add(PodcastWithEpisodes feed)
		{
			Feeds.RemoveAll(f => f.Podcast.Uri == feed.Podcast.Uri);
			Feeds.Add(feed);
			Version.Value = Version.Peek() + 1;
		}

		public static IReadOnlyList<PodcastWithEpisodes> All => Feeds;

		public static IReadOnlyList<Category> Categories =>
			Feeds.SelectMany(f => f.Categories).DistinctBy(c => c.Name)
				.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();

		public static IReadOnlyList<PodcastWithEpisodes> InCategory(Category category) =>
			Feeds.Where(f => f.Categories.Any(c => c.Name == category.Name)).ToArray();

		/// <summary>Newest-first episode rows for a set of feeds (the gold's
		/// episodesInCategory / latest-library queries).</summary>
		public static IReadOnlyList<EpisodeToPodcast> LatestEpisodes(IEnumerable<PodcastWithEpisodes> feeds, int limit = 20) =>
			feeds.SelectMany(f => f.Episodes.Select(e => new EpisodeToPodcast(e, f.Podcast)))
				.OrderByDescending(e => e.Episode.Published).Take(limit).ToArray();

		public static Podcast? GetPodcast(string uri) =>
			Feeds.FirstOrDefault(f => f.Podcast.Uri == uri)?.Podcast;

		public static IReadOnlyList<Episode> EpisodesFor(string podcastUri) =>
			Feeds.FirstOrDefault(f => f.Podcast.Uri == podcastUri)?.Episodes ?? Array.Empty<Episode>();

		public static Episode? GetEpisode(string uri) =>
			Feeds.SelectMany(f => f.Episodes).FirstOrDefault(e => e.Uri == uri);

		public static bool IsFollowed(string podcastUri) => Followed.Contains(podcastUri);

		public static IReadOnlyList<PodcastWithEpisodes> FollowedFeeds =>
			Feeds.Where(f => Followed.Contains(f.Podcast.Uri)).ToArray();

		public static void ToggleFollowed(string podcastUri)
		{
			if (!Followed.Remove(podcastUri))
				Followed.Add(podcastUri);
			Version.Value = Version.Peek() + 1;
		}
	}

	/// <summary>
	/// Fixture mode: the six bundled feed snapshots (sample/Shared/Jetcaster/fixtures,
	/// trimmed to 10 episodes each) + their bundled 600px artwork. The probe wires
	/// <paramref name="openAsset"/> per platform (Android Assets.Open, iOS bundle path)
	/// — the same pattern as font registration.
	/// </summary>
	public static class JetcasterFixtures
	{
		// Fixture file → bundled artwork image name (bare name resolves to the
		// platform image resource, like the Jetsnack jpgs).
		public static readonly IReadOnlyList<(string Feed, string Art)> Files = new[]
		{
			("now_in_android.rss", "now_in_android_art"),
			("adb_backstage.rss", "adb_backstage_art"),
			("this_american_life.rss", "this_american_life_art"),
			("reply_all.rss", "reply_all_art"),
			("ninety_nine_pi.rss", "ninety_nine_pi_art"),
			("no_such_thing_as_a_fish.rss", "no_such_thing_as_a_fish_art"),
		};

		static bool _loaded;

		public static void Load(Func<string, System.IO.Stream> openAsset)
		{
			if (_loaded)
				return;
			_loaded = true;
			foreach (var (feed, art) in Files)
			{
				try
				{
					using var stream = openAsset(feed);
					var parsed = JetcasterFeedParser.Parse(stream);
					// Point the artwork at the BUNDLED image (deterministic offline
					// renders) instead of the snapshot's remote URL.
					parsed = parsed with { Podcast = parsed.Podcast with { ImageUrl = art } };
					PodcastStore.Add(parsed);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[Jetcaster] fixture {feed} failed: {ex.Message}");
				}
			}
		}
	}
}
