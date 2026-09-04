#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace CometSamples.Jetcaster
{
	/// <summary>
	/// RSS 2.0 + iTunes-namespace feed parser — the C# twin of the gold's ROME
	/// SyndFeed mapping (PodcastFetcher.kt:120-157: title/description/author/image
	/// from the iTunes module with RSS fallbacks; episodes keyed by guid). Pure
	/// (stream in → records out) so fixture and live modes share it.
	/// </summary>
	public static class JetcasterFeedParser
	{
		static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

		public static PodcastWithEpisodes Parse(System.IO.Stream stream)
		{
			var doc = XDocument.Load(stream);
			var channel = doc.Root?.Element("channel")
				?? throw new FormatException("not an RSS feed (no <channel>)");

			string podcastUri = Value(channel.Element("link")) ?? Value(channel.Element("title"))
				?? throw new FormatException("feed has no link/title");

			var podcast = new Podcast(
				Uri: podcastUri,
				Title: Value(channel.Element("title")) ?? "",
				Description: Value(channel.Element(Itunes + "summary")) ?? Value(channel.Element("description")),
				Author: Value(channel.Element(Itunes + "author")),
				ImageUrl: channel.Element(Itunes + "image")?.Attribute("href")?.Value
					?? Value(channel.Element("image")?.Element("url")),
				Copyright: Value(channel.Element("copyright")));

			// The gold reads the iTunes category tree (nested <itunes:category text=…>).
			var categories = channel.Elements(Itunes + "category")
				.SelectMany(c => new[] { c }.Concat(c.Elements(Itunes + "category")))
				.Select(c => c.Attribute("text")?.Value)
				.Where(t => !string.IsNullOrEmpty(t))
				.Select(t => new Category(t!))
				.DistinctBy(c => c.Name)
				.ToArray();

			var episodes = channel.Elements("item").Select(item => new Episode(
					Uri: Value(item.Element("guid")) ?? Value(item.Element("link")) ?? Guid.NewGuid().ToString(),
					PodcastUri: podcastUri,
					Title: Value(item.Element("title")) ?? "",
					Subtitle: Value(item.Element(Itunes + "subtitle")),
					Summary: Value(item.Element(Itunes + "summary")) ?? Value(item.Element("description")),
					Author: Value(item.Element(Itunes + "author")),
					Published: ParseDate(Value(item.Element("pubDate"))),
					Duration: ParseDuration(Value(item.Element(Itunes + "duration")))))
				.OrderByDescending(e => e.Published)
				.ToArray();

			return new PodcastWithEpisodes(podcast, episodes, categories);
		}

		static string? Value(XElement? e)
		{
			var v = e?.Value.Trim();
			return string.IsNullOrEmpty(v) ? null : v;
		}

		// RFC 1123-ish pubDate ("Sun, 12 Jul 2026 06:00:00 -0400" / "… GMT" / "… EST").
		static DateTimeOffset ParseDate(string? s)
		{
			if (string.IsNullOrEmpty(s))
				return default;
			if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
				return dto;
			// Named zones DateTimeOffset can't parse (EST/PDT…) — strip and treat as UTC.
			int lastSpace = s.LastIndexOf(' ');
			if (lastSpace > 0 && DateTimeOffset.TryParse(s[..lastSpace], CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal, out dto))
				return dto;
			return default;
		}

		// itunes:duration: "HH:MM:SS", "MM:SS", or bare seconds.
		static TimeSpan? ParseDuration(string? s)
		{
			if (string.IsNullOrEmpty(s))
				return null;
			var parts = s.Split(':');
			return parts.Length switch
			{
				1 when long.TryParse(parts[0], out var secs) => TimeSpan.FromSeconds(secs),
				2 when int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var sec) =>
					new TimeSpan(0, m, sec),
				3 when int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m2)
					&& int.TryParse(parts[2], out var s2) => new TimeSpan(h, m2, s2),
				_ => null,
			};
		}
	}
}
