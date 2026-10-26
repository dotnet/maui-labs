#nullable enable
using System.Collections.Generic;
using System.Linq;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.JetNews.JetNewsTheme;

namespace CometSamples.JetNews
{
	/// <summary>
	/// The Article (post) screen, values-from-source: PostScreen.kt (chrome) +
	/// PostContent.kt (body). Compact chrome = center top bar ("Published in: \n{name}"
	/// next to the publication badge, back arrow tinted primary) + bottom action bar;
	/// content = header image / headlineLarge title / subtitle / metadata / typed
	/// paragraphs with markups (bold, italic, underline links, monospace code spans).
	/// </summary>
	public static class JetNewsArticle
	{
		const float SpacerSize = 16f;   // PostContent.kt defaultSpacerSize

		// ── Rows: PostContent.kt postContentItems — header item, metadata item, one item
		// per paragraph (a real lazy list, like the gold LazyColumn). ──
		abstract record ArticleRow;
		sealed record HeaderRow(Post Post) : ArticleRow;
		sealed record MetadataRow(Post Post) : ArticleRow;
		sealed record ParagraphRow(Paragraph Paragraph) : ArticleRow;

		public static View Screen(Comet.Reactive.Signal<Post> post, double topInset, System.Action onBack)
		{
			JetNewsIcons.Register();

			static List<ArticleRow> Rows(Post p)
			{
				var rows = new List<ArticleRow> { new HeaderRow(p), new MetadataRow(p) };
				rows.AddRange(p.Paragraphs.Select(pg => (ArticleRow)new ParagraphRow(pg)));
				return rows;
			}

			var list = new ListView<ArticleRow>(() => Rows(post.Peek()))
			{
				ViewFor = r => r switch
				{
					HeaderRow h => Header(h.Post),
					MetadataRow m => Metadata(m.Post.Metadata),
					ParagraphRow p => ParagraphView(p.Paragraph),
					_ => new HStack(),
				},
			};
			// One retained shell serves every post: swapping CurrentPost re-pulls the rows.
			// Subscribed ONCE to the (static) signal via the current-list slot — a per-Screen
			// subscription would leak a handler + captured list per rebuild.
			_currentList = list;
			if (!_postHooked)
			{
				_postHooked = true;
				post.PropertyChanged += (_, _) => _currentList?.ReloadData();
			}

			return new VStack(spacing: 0f)
			{
				new HStack().Frame(height: (float)topInset).FlexShrink(0),
				TopBar(post, onBack).FlexShrink(0),
				list.FlexGrow(1).FlexBasis(0),
				BottomBar().FlexShrink(0),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.Background);
		}

		static ListView<ArticleRow>? _currentList;
		static bool _postHooked;

		static Text Tx(string s) => new Text(s).FontFamily("Montserrat");

		// CenterAlignedTopAppBar (PostScreen.kt): back arrow tinted primary; centered title =
		// publication badge (36dp circle; the gold vector is an Android head on #073042 —
		// approximated with the Material Icons android glyph on the same navy) + two-line
		// "Published in: \n{name}" labelLarge, 8dp start padding, text left-aligned.
		static View TopBar(Comet.Reactive.Signal<Post> post, System.Action onBack) => new HStack(spacing: 0f)
		{
			new Icon("arrow_back").IconSize(24).Color(T.Primary)
				.Frame(width: 48, height: 48).Padding(new Thickness(12))
				.FlexShrink(0)
				.OnTap(_ => onBack()),
			new HStack().FlexGrow(1),
			// The gold icon_post_background vector (multicolor — renders untinted).
			new Icon("jetnews_badge").IconSize(36)
				.Frame(width: 36, height: 36).CornerRadius(18)
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.FlexShrink(0),
			new VStack(spacing: 0f)
			{
				Tx("Published in:").FontSize(14).FontWeight(FontWeight.Medium).Color(T.OnSurface),
				new Text(() => post.Value.Publication?.Name ?? string.Empty).FontFamily("Montserrat")
					.FontSize(14).FontWeight(FontWeight.Medium).Color(T.OnSurface),
			}.Padding(new Thickness(8, 0, 0, 0)).VerticalLayoutAlignment(LayoutAlignment.Center),
			new HStack().FlexGrow(1),
			new HStack().Frame(width: 48).FlexShrink(0),   // balances the nav slot
		}.Frame(height: 64);

		// PostContent.kt item 1: header image (minH 180, full width, shapes.large = r8),
		// spacer 16, headlineLarge title, spacer 8, bodyMedium subtitle + spacer 16.
		static View Header(Post post)
		{
			var stack = new VStack(spacing: 0f)
			{
				new Image(post.ImageId).Frame(height: 180).CornerRadius(8)
					.HorizontalLayoutAlignment(LayoutAlignment.Fill),
				new HStack().Frame(height: SpacerSize),
				Tx(post.Title).FontSize(32).LineHeight(40).Color(T.OnSurface)
					.LineBreakMode(LineBreakMode.WordWrap).LineBreak(TextLineBreak.Heading),
				new HStack().Frame(height: 8),
			};
			if (post.Subtitle is { } subtitle)
			{
				stack.Add(Tx(subtitle).FontSize(14).LineHeight(20).Color(T.OnSurface)
					.LineBreakMode(LineBreakMode.WordWrap).LineBreak(TextLineBreak.Paragraph));
				stack.Add(new HStack().Frame(height: SpacerSize));
			}
			return stack.Padding(new Thickness(16, 0, 16, 0));
		}

		// PostContent.kt PostMetadata: account_circle 40 tinted content color, 8 gap,
		// author labelLarge (padded 4 from top) over "{date} · {read} min read" bodySmall;
		// 24dp bottom padding.
		static View Metadata(Metadata metadata) => new HStack(spacing: 0f)
		{
			new Icon("account_circle").IconSize(40).Color(T.OnSurface)
				.Frame(width: 40, height: 40).FlexShrink(0),
			new VStack(spacing: 0f)
			{
				Tx(metadata.Author.Name).FontSize(14).FontWeight(FontWeight.Medium).Color(T.OnSurface)
					.Padding(new Thickness(0, 4, 0, 0)),
				Tx($"{metadata.Date} · {metadata.ReadTimeMinutes} min read")
					.FontSize(12).Color(T.OnSurface),
			}.Padding(new Thickness(8, 0, 0, 0)),
		}.Padding(new Thickness(16, 0, 16, 24));

		// ── Paragraph (PostContent.kt): style per ParagraphType; markups become styled runs.
		// Trailing padding 24 (16 for Subhead/Header); non-block text carries the gold's 4dp pad. ──
		static View ParagraphView(Paragraph paragraph)
		{
			var (fontSize, lineHeight, weight, trailing) = paragraph.Type switch
			{
				ParagraphType.Caption => (12f, 16f, FontWeight.Medium, 24f),
				ParagraphType.Title => (32f, 40f, FontWeight.Regular, 24f),
				ParagraphType.Subhead => (24f, 32f, FontWeight.Regular, 16f),
				ParagraphType.Header => (28f, 36f, FontWeight.Regular, 16f),
				_ => (16f, 28f, FontWeight.Regular, 24f),   // Text/CodeBlock/Quote/Bullet = bodyLarge (Text pins lineHeight 28)
			};

			View content = paragraph.Type switch
			{
				ParagraphType.CodeBlock => CodeBlock(paragraph, fontSize),
				ParagraphType.Bullet => Bullet(paragraph, fontSize, lineHeight),
				_ => Runs(paragraph, fontSize, lineHeight, weight,
						heading: paragraph.Type is ParagraphType.Title or ParagraphType.Subhead or ParagraphType.Header)
					.Padding(new Thickness(4)),
			};

			return new VStack(spacing: 0f) { content }
				.Padding(new Thickness(16, 0, 16, trailing));
		}

		/// <summary>The paragraph text with its markups as styled runs — bold / italic /
		/// underlined link / monospace code span on onSurface@15% (PostContent.kt markup map).</summary>
		static View Runs(Paragraph paragraph, float fontSize, float lineHeight, FontWeight weight, bool heading)
		{
			var view = paragraph.Markups is { Count: > 0 }
				? (View)new FormattedText(BuildRuns(paragraph))
				: Tx(paragraph.Text);
			return view
				.FontSize(fontSize).LineHeight(lineHeight).FontWeight(weight)
				.FontFamily("Montserrat").Color(T.OnSurface)
				.LineBreakMode(LineBreakMode.WordWrap)
				.LineBreak(heading ? TextLineBreak.Heading : TextLineBreak.Paragraph);
		}

		static IReadOnlyList<TextRun> BuildRuns(Paragraph paragraph)
		{
			var text = paragraph.Text;
			var runs = new List<TextRun>();
			int cursor = 0;
			foreach (var m in paragraph.Markups!.OrderBy(m => m.Start))
			{
				int start = System.Math.Clamp(m.Start, 0, text.Length);
				int end = System.Math.Clamp(m.End, start, text.Length);
				if (start < cursor)
					continue;   // overlapping markups: first one wins (gold data has none)
				if (start > cursor)
					runs.Add(new TextRun(text[cursor..start]));
				runs.Add(m.Type switch
				{
					MarkupType.Bold => new TextRun(text[start..end], Bold: true),
					MarkupType.Italic => new TextRun(text[start..end], Italic: true),
					MarkupType.Link => new TextRun(text[start..end], Underline: true),
					_ => new TextRun(text[start..end], Monospace: true, Background: T.CodeBlockBackground),
				});
				cursor = end;
			}
			if (cursor < text.Length)
				runs.Add(new TextRun(text[cursor..]));
			return runs;
		}

		// CodeBlockParagraph: full-width Surface, shapes.small = r4, onSurface@15%, pad 16,
		// monospace bodyLarge.
		static View CodeBlock(Paragraph paragraph, float fontSize) => new VStack(spacing: 0f)
		{
			new FormattedText(new[] { new TextRun(paragraph.Text, Monospace: true) })
				.FontSize(fontSize).LineHeight(24).Color(T.OnSurface)
				.LineBreakMode(LineBreakMode.WordWrap)
				.Padding(new Thickness(16)),
		}
		.Background(T.CodeBlockBackground).CornerRadius(4)
		.HorizontalLayoutAlignment(LayoutAlignment.Fill);

		// BulletParagraph: an 8sp dot aligned to the first line + indented text.
		static View Bullet(Paragraph paragraph, float fontSize, float lineHeight) => new HStack(spacing: 0f)
		{
			new HStack().Frame(width: 8, height: 8)
				.Background(T.OnSurface).CornerRadius(4)
				.Margin(new Thickness(0, (lineHeight - 8) / 2, 8, 0))
				.VerticalLayoutAlignment(LayoutAlignment.Start).FlexShrink(0),
			Runs(paragraph, fontSize, lineHeight, FontWeight.Regular, heading: false)
				.FlexGrow(1).FlexBasis(0),
		};

		// BottomAppBar (PostScreen.kt bottomBarContent): M3 tokens — 80dp, surfaceContainer,
		// leading 16; actions thumb_up / bookmark toggle / share / text_format.
		static View BottomBar()
		{
			View Action(string icon, System.Action? onTap = null) =>
				new Icon(icon).IconSize(24).Color(T.OnSurfaceVariant)
					.Frame(width: 48, height: 48).Padding(new Thickness(12))
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.OnTap(_ => onTap?.Invoke());

			return new HStack(spacing: 0f)
			{
				Action("thumb_up_offalt"),
				Action("bookmark_border"),
				Action("share"),
				Action("text_format"),
			}
			.Padding(new Thickness(16, 0, 16, 0))
			.Frame(height: 80)
			.Background(T.SurfaceContainer)
			.HorizontalLayoutAlignment(LayoutAlignment.Fill);
		}
	}
}
