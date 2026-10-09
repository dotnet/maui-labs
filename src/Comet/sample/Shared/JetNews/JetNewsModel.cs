#nullable enable
using System.Collections.Generic;

namespace CometSamples.JetNews
{
	// Verbatim port of the gold model (model/Post.kt) — same names, same shape.
	// Image ids become bundled resource NAMES (drawables on Android / bundle images on iOS).

	public sealed record Post(
		string Id,
		string Title,
		string? Subtitle,
		string Url,
		Publication? Publication,
		Metadata Metadata,
		IReadOnlyList<Paragraph> Paragraphs,
		string ImageId,
		string ImageThumbId);

	public sealed record Metadata(PostAuthor Author, string Date, int ReadTimeMinutes);

	public sealed record PostAuthor(string Name, string? Url = null);

	public sealed record Publication(string Name, string LogoUrl);

	public sealed record Paragraph(ParagraphType Type, string Text, IReadOnlyList<Markup>? Markups = null);

	public sealed record Markup(MarkupType Type, int Start, int End, string? Href = null);

	public enum MarkupType { Link, Code, Italic, Bold }

	public enum ParagraphType { Title, Caption, Header, Subhead, Text, CodeBlock, Quote, Bullet }

	// model/PostsFeed.kt
	public sealed record PostsFeed(
		Post HighlightedPost,
		IReadOnlyList<Post> RecommendedPosts,
		IReadOnlyList<Post> PopularPosts,
		IReadOnlyList<Post> RecentPosts)
	{
		public IReadOnlyList<Post> AllPosts
		{
			get
			{
				var all = new List<Post> { HighlightedPost };
				all.AddRange(RecommendedPosts);
				all.AddRange(PopularPosts);
				all.AddRange(RecentPosts);
				return all;
			}
		}
	}
}
