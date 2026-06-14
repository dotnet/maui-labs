#nullable enable
using System.Collections.Generic;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;

namespace CometSamples.Jetchat
{
	/// <summary>
	/// A faithful Comet port of the conversation screen from Google's official
	/// <c>android/compose-samples</c> <b>Jetchat</b> (the gold standard for a Compose-vs-Comet
	/// comparison). Rendered on the node backend (Yoga layout, no MAUI handlers), it mirrors the
	/// real screen: the centered <c>#composers</c> channel bar, the author-grouped message log
	/// (42dp circular avatars, name+timestamp headers, the iconic <c>RoundedCornerShape(4,20,20,20)</c>
	/// chat bubbles in primary/surfaceVariant, the 160dp sticker), and the bottom input bar.
	/// Colors, spacing, shapes, the nine messages and the real avatar/sticker assets are taken
	/// straight from the sample.
	/// </summary>
	public static class JetchatConversation
	{
		// Semantic color roles come from the centralized JetchatTheme (mirrors theme/Themes.kt);
		// these are local aliases so the screen reads tokens, not hardcoded hex.
		internal static readonly Color Primary = JetchatTheme.Primary;
		internal static readonly Color OnPrimary = JetchatTheme.OnPrimary;
		internal static readonly Color Surface = JetchatTheme.Surface;
		internal static readonly Color OnSurface = JetchatTheme.OnSurface;
		internal static readonly Color SurfaceVariant = JetchatTheme.SurfaceVariant;
		internal static readonly Color OnSurfaceVariant = JetchatTheme.OnSurfaceVariant;
		internal static readonly Color Tertiary = JetchatTheme.Tertiary;
		internal static readonly Color Divider = JetchatTheme.Divider;
		internal static readonly Color BarSurface = JetchatTheme.SurfaceTinted;
		internal static readonly Color Disabled = JetchatTheme.Disabled;

		// Bundled drawables (Jetchat ships these as local resources via painterResource, so they
		// render offline and without a network round-trip). The backends resolve a bare name to a
		// platform resource: Android drawable / iOS bundle image.
		internal const string AvatarMe = "ali";
		internal const string AvatarOther = "someone_else";
		const string Sticker = "sticker";

		sealed record Msg(string Author, string Content, string Timestamp, bool HasImage = false);

		// FakeData.initialMessages is newest-first (the LazyColumn is reverseLayout); displayed
		// top-to-bottom it is the reverse — oldest at top. Authored exactly as the sample.
		static readonly List<Msg> Display = new()
		{
			new("John Glenn", "Yeah its seems to be pretty new!", "8:12 PM"),
			new("Taylor Brooks", "Wow! I never knew about Glance Widgets when was this added to the android ecosystem", "8:10 PM"),
			new("Shangeeth Sivan", "Does anyone know about Glance Widgets its the new way to build widgets in Android!", "8:08 PM"),
			new("me", "Compose newbie: I’ve scourged the internet for tutorials about async data loading but haven’t found any good ones 🫠☁️. What’s the recommended way to load async data and emit composable widgets?", "8:03 PM"),
			new("John Glenn", "Compose newbie as well 🦩, have you looked at the JetNews sample? Most blog posts end up out of date pretty fast but this sample is always up to date and deals with async data loading (it’s faked but the same idea applies) 👉 https://goo.gle/jetnews", "8:04 PM"),
			new("Taylor Brooks", "@aliconors Take a look at the `Flow.collectAsStateWithLifecycle()` APIs", "8:05 PM"),
			new("Taylor Brooks", "You can use all the same stuff", "8:05 PM"),
			new("me", "Thank you!🥷", "8:06 PM", HasImage: true),
			new("me", "Check it out!", "8:07 PM"),
		};

		/// <summary>Builds the conversation screen. <paramref name="topInset"/>/<paramref name="bottomInset"/>
		/// are the platform safe-area insets (status bar / home indicator) in Dp.</summary>
		internal static readonly Comet.Reactive.Signal<bool> DrawerOpen = new(false);

		/// <summary>The Jetchat sample: conversation behind the navigation drawer. (The profile
		/// detail via <see cref="JetchatApp"/> is wired but blocked on root-Component body swap
		/// support in the node backend — tapping a drawer profile currently just closes the drawer.)</summary>
		public static View Build(double topInset = 24, double bottomInset = 0) =>
			new Drawer(DrawerOpen, JetchatDrawer.Content(topInset), ConversationView(topInset, bottomInset));

		/// <summary>The conversation screen (drawer content slot). Public for <see cref="JetchatApp"/>.</summary>
		internal static View ConversationView(double topInset, double bottomInset) => new VStack(spacing: 0f)
		{
			ChannelNameBar(topInset),

			// The message log scrolls between the fixed bars.
			new ScrollView
			{
				MessageLog(),
			}.FillVertical(),

			UserInput(bottomInset),
		}.Background(Surface);

		// ── Channel bar: ‹jetchat-logo⟩  ⟨#composers / 42 members⟩  ⌕  ⓘ ── (no bottom line; the
		// background tint separates it from the body). Mirrors a CenterAlignedTopAppBar: equal-flex
		// side zones screen-center the title; 16dp edge insets via the bar's own padding (leaf
		// padding is ignored by the layout engine, so spacing comes from the zones/margins). ──
		static View ChannelNameBar(double topInset) => new HStack(spacing: 0f)
		{
			// Left zone (flex): the jetchat logo at the start; the spacer balances the right zone.
			new HStack(spacing: 0f)
			{
				new Icon("jetchat").Color(Primary).IconSize(28)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.OnTap(_ => DrawerOpen.Value = true),   // tap the logo → open the nav drawer
				Spacer(),
			}.FlexGrow(1).FlexBasis(0),

			new VStack(spacing: 0f)
			{
				new Text("#composers").Color(OnSurface).TitleMedium()
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
				new Text("42 members").Color(OnSurfaceVariant).BodySmall()
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
			}.VerticalLayoutAlignment(LayoutAlignment.Center),

			// Right zone (flex, equal weight → title sits at true screen-center): the actions at the end.
			new HStack(spacing: 0f)
			{
				Spacer(),
				BarIcon("search").Margin(right: 20),
				BarIcon("info"),
			}.FlexGrow(1).FlexBasis(0),
		}.Padding(new Thickness(16, topInset + 12, 16, 12)).Background(BarSurface);

		// A real Material Icon (ImageVector / SF Symbol), 24dp, tinted onSurfaceVariant, centered.
		static View BarIcon(string symbol) =>
			new Icon(symbol).Color(OnSurfaceVariant).IconSize(24)
				.VerticalLayoutAlignment(LayoutAlignment.Center);

		// ── Message log ──
		static View MessageLog()
		{
			var stack = new VStack(spacing: 0f)
			{
				DayHeader("Today"),
			};

			for (int i = 0; i < Display.Count; i++)
			{
				var m = Display[i];
				bool topOfGroup = i == 0 || Display[i - 1].Author != m.Author;       // avatar + name here
				bool bottomOfGroup = i == Display.Count - 1 || Display[i + 1].Author != m.Author;
				stack.Add(MessageRow(m, topOfGroup, bottomOfGroup ? 8 : 4));
			}

			return stack;
		}

		static View MessageRow(Msg m, bool topOfGroup, double bottomSpace)
		{
			bool isMe = m.Author == "me";

			// Left gutter: a 42dp avatar (author ring + a surface "halo" gap around the photo) on
			// the group's top message; a 74dp spacer otherwise — so a run of messages by one author
			// lines up under the avatar. The 3px surface padding inside the ring is the halo.
			View gutter = topOfGroup
				? new VStack(spacing: 0f)
					{
						new Image(isMe ? AvatarMe : AvatarOther).Frame(width: 36, height: 36).CornerRadius(18),
					}
						.Frame(width: 42, height: 42)
						.Padding(new Thickness(3))
						.Background(Surface)
						.CornerRadius(21)
						.Border(2, isMe ? Primary : Tertiary)
						.Margin(left: 16, right: 16)
						.VerticalLayoutAlignment(LayoutAlignment.Start)   // stay 42×42, don't stretch to row height
				: new HStack().Frame(width: 74);

			var column = new VStack(spacing: 0f);
			if (topOfGroup)
				column.Add(AuthorNameTimestamp(m));
			column.Add(Bubble(m.Content, isMe));
			if (m.HasImage)
			{
				column.Add(new HStack().Frame(height: 4));            // 4dp gap to the image bubble
				column.Add(StickerBubble(isMe));
			}
			column.Add(new HStack().Frame(height: (float)bottomSpace));

			return new HStack(spacing: 0f)
			{
				gutter,
				column.FlexGrow(1).Padding(new Thickness(0, 0, 16, 0)),
			}.Padding(new Thickness(0, topOfGroup ? 8 : 0, 0, 0));
		}

		static View AuthorNameTimestamp(Msg m) => new HStack(spacing: 8f)
		{
			new Text(m.Author).Color(OnSurface).TitleMedium(),
			new Text(m.Timestamp).Color(OnSurfaceVariant).BodySmall(),
		}.Padding(new Thickness(0, 0, 0, 4));

		// The iconic chat bubble: RoundedCornerShape(4,20,20,20); primary/white for me,
		// surfaceVariant/onSurface for others. Left-aligned and hugging its content (it grows to
		// the column width only when the text wraps).
		static View Bubble(string content, bool isMe) => new VStack(spacing: 0f)
		{
			new FormattedText(FormatMessage(content, isMe)).BodyLarge(),
		}
			.Padding(new Thickness(16))
			.Background(isMe ? Primary : SurfaceVariant)
			.CornerRadius(JetchatTheme.BubbleTopStart, JetchatTheme.BubbleOther, JetchatTheme.BubbleOther, JetchatTheme.BubbleOther)
			.HorizontalLayoutAlignment(LayoutAlignment.Start);

		// The C# port of Jetchat's messageFormatter: @mentions and links take the accent color
		// (links underlined), `code` spans render monospace in a tonal box. On my (primary) bubbles
		// the accent is the on-primary color; on others it's the primary blue.
		static System.Collections.Generic.IReadOnlyList<TextRun> FormatMessage(string content, bool isMe)
		{
			var baseColor = isMe ? OnPrimary : OnSurface;
			var accent = isMe ? OnPrimary : Primary;
			var codeBg = isMe ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#14000000");

			var runs = new System.Collections.Generic.List<TextRun>();
			var parts = content.Split('`');
			for (int i = 0; i < parts.Length; i++)
			{
				if (i % 2 == 1)             // odd segments are between backticks → code
				{
					if (parts[i].Length > 0)
						runs.Add(new TextRun(parts[i], Color: baseColor, Monospace: true, Background: codeBg));
					continue;
				}
				foreach (var token in System.Text.RegularExpressions.Regex.Split(parts[i], @"(\s+)"))
				{
					if (token.Length == 0)
						continue;
					if (token.StartsWith("@"))
						runs.Add(new TextRun(token, Color: accent));
					else if (token.StartsWith("http"))
						runs.Add(new TextRun(token, Color: accent, Underline: true));
					else
						runs.Add(new TextRun(token, Color: baseColor));
				}
			}
			return runs;
		}

		static View StickerBubble(bool isMe) => new VStack(spacing: 0f)
		{
			new Image(Sticker).Frame(width: 160, height: 160),
		}
			.Background(isMe ? Primary : SurfaceVariant)
			.CornerRadius(JetchatTheme.BubbleTopStart, JetchatTheme.BubbleOther, JetchatTheme.BubbleOther, JetchatTheme.BubbleOther)
			.HorizontalLayoutAlignment(LayoutAlignment.Start);

		// Row of: rule — "Today" — rule, all vertically centered (the rule aligns to the text's
		// middle). Text given full height + center alignment so it isn't cropped.
		static View DayHeader(string day) => new HStack(spacing: 0f)
		{
			HLine().FlexGrow(1).VerticalLayoutAlignment(LayoutAlignment.Center),
			new Text(day).Color(OnSurfaceVariant).LabelSmall()
				.VerticalLayoutAlignment(LayoutAlignment.Center).Padding(new Thickness(16, 0, 16, 0)),
			HLine().FlexGrow(1).VerticalLayoutAlignment(LayoutAlignment.Center),
		}.Padding(new Thickness(16, 8, 16, 8));

		// ── Bottom input bar (same tint as the header; no top line). Row 1: a borderless,
		// full-width text field. Row 2: the blue action icons spread evenly to a disabled Send. ──
		static View UserInput(double bottomInset) => new VStack(spacing: 0f)
		{
			new TextField(new Comet.Reactive.Signal<string>(string.Empty), "Message #composers")
				.Padding(new Thickness(20, 14, 20, 10)),
			new HStack(spacing: 0f)
			{
				InputIcon("mood"), Spacer(), InputIcon("at"), Spacer(), InputIcon("photo"),
				Spacer(), InputIcon("place"), Spacer(), InputIcon("video"), Spacer(),
				// Disabled Send: a real Material OutlinedButton (bordered, no fill) with grey
				// content — exactly the sample's disabled Send button, not a styled label.
				new Button("Send", () => { })
					.Outlined()
					.LabelLarge()
					.Color(Disabled)
					.CornerRadius(18)
					.Frame(height: 36)
					.VerticalLayoutAlignment(LayoutAlignment.Center),
			}.Padding(new Thickness(16, 4, 16, 12 + bottomInset)),
		}.Background(BarSurface);

		static View InputIcon(string symbol) => new Icon(symbol).Color(Primary).IconSize(24)
			.VerticalLayoutAlignment(LayoutAlignment.Center);

		static View Spacer() => new HStack().FlexGrow(1);

		// A 1dp hairline in the divider color (used for the day header rule).
		static View HLine() => new HStack().Frame(height: 1).Background(Divider);
	}
}
