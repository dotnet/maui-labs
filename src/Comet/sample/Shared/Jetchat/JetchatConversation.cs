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
		// Read live from JetchatTheme (computed, not cached) so a runtime scheme swap (Material You,
		// JetchatTheme.ApplyScheme) reaches these before the tree is built.
		internal static Color Primary => JetchatTheme.Primary;
		internal static Color OnPrimary => JetchatTheme.OnPrimary;
		internal static Color Secondary => JetchatTheme.Secondary;
		internal static Color Surface => JetchatTheme.Surface;
		internal static Color OnSurface => JetchatTheme.OnSurface;
		internal static Color SurfaceVariant => JetchatTheme.SurfaceVariant;
		internal static Color OnSurfaceVariant => JetchatTheme.OnSurfaceVariant;
		internal static Color Tertiary => JetchatTheme.Tertiary;
		internal static Color Divider => JetchatTheme.Divider;
		internal static Color BarSurface => JetchatTheme.SurfaceTinted;
		internal static Color Disabled => JetchatTheme.Disabled;

		// Bundled drawables (Jetchat ships these as local resources via painterResource, so they
		// render offline and without a network round-trip). The backends resolve a bare name to a
		// platform resource: Android drawable / iOS bundle image.
		internal const string AvatarMe = "ali";
		internal const string AvatarOther = "someone_else";
		const string Sticker = "sticker";

		sealed record Msg(string Author, string Content, string Timestamp, bool HasImage = false);

		// FakeData.initialMessages is newest-first (the LazyColumn is reverseLayout); displayed
		// top-to-bottom it is the reverse — oldest at top. Authored exactly as the sample.
		static readonly List<Msg> Display = BuildDisplay();

		static List<Msg> BuildDisplay()
		{
			var list = new List<Msg>
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

			// ~100 extra messages so the LazyColumn actually virtualizes/recycles while scrolling.
			// Varied authors + lengths (some wrap, some carry @mentions / `code` / links) so each row
			// re-measures realistically rather than all being identical.
			string[] authors = { "John Glenn", "Taylor Brooks", "Shangeeth Sivan", "me", "Ali Conors" };
			string[] bodies =
			{
				"👍",
				"Sounds good to me",
				"A medium-length reply that wraps onto a second line on most phones for layout variety",
				"@aliconors did you try the `rememberLazyListState()` API for scroll position?",
				"Here's the doc with the full async-loading walkthrough 👉 https://goo.gle/jetnews",
				"Longer one: virtualization means only the rows on screen are composed and measured, so a list of hundreds should scroll just as smoothly as a list of nine — that's the whole point of LazyColumn 🚀",
				"`collectAsStateWithLifecycle()` is the move",
				"Nice 🔥",
			};
			for (int i = 0; i < 100; i++)
				list.Add(new Msg(authors[i % authors.Length], bodies[i % bodies.Length], $"7:{59 - i % 60:D2} PM"));

			return list;
		}

		// Flattened rows for the message LazyColumn: the "Today" header plus each message tagged with
		// its grouping (top-of-group shows the avatar + author; bottom-of-group gets extra spacing).
		abstract record Row;
		sealed record HeaderRow(string Day) : Row;
		sealed record MsgRow(Msg M, bool TopOfGroup, double BottomSpace) : Row;

		static readonly List<Row> Rows = BuildRows();

		static List<Row> BuildRows()
		{
			var rows = new List<Row> { new HeaderRow("Today") };
			for (int i = 0; i < Display.Count; i++)
			{
				var m = Display[i];
				bool topOfGroup = i == 0 || Display[i - 1].Author != m.Author;       // avatar + name here
				bool bottomOfGroup = i == Display.Count - 1 || Display[i + 1].Author != m.Author;
				rows.Add(new MsgRow(m, topOfGroup, bottomOfGroup ? 8 : 4));
			}
			return rows;
		}

		/// <summary>Builds the conversation screen. <paramref name="topInset"/>/<paramref name="bottomInset"/>
		/// are the platform safe-area insets (status bar / home indicator) in Dp.</summary>
		internal static readonly Comet.Reactive.Signal<bool> DrawerOpen = new(false);

		// Drives the "Functionality not available 🙊" AlertDialog (Jetchat's NotAvailablePopup),
		// opened from the DM ("@") input selector — exactly `InputSelector.DM -> NotAvailablePopup`.
		static readonly Comet.Reactive.Signal<bool> NotAvailableOpen = new(false);

		// The composer's text, two-way bound to the input field. The Send button reads it to toggle
		// enabled/style, and a send appends a "me" message + clears it (UserInput.kt sendMessageEnabled).
		static readonly Comet.Reactive.Signal<string> InputText = new(string.Empty);

		/// <summary>The Jetchat sample, rooted in a real <see cref="NavigationView"/> (the C# port of
		/// Jetchat's NavActivity nav graph): the conversation-behind-the-drawer is the root screen;
		/// tapping a drawer profile closes the drawer and <c>Navigate</c>s the profile detail onto the
		/// stack; the profile's back arrow <c>Pop</c>s it.</summary>
		public static View Build(double topInset = 24, double bottomInset = 0)
		{
			var nav = new NavigationView();
			void OpenProfile(string profileName)
			{
				DrawerOpen.Value = false;                                  // close the drawer …
				nav.Navigate(JetchatProfile.Screen(profileName, topInset, bottomInset, () => nav.Pop())); // … and push the profile
			}
			nav.Content = new Drawer(DrawerOpen, JetchatDrawer.Content(topInset, OpenProfile), ConversationView(topInset, bottomInset));
			return nav;
		}

		/// <summary>The conversation screen (the drawer's content slot / nav root screen).</summary>
		internal static View ConversationView(double topInset, double bottomInset)
		{
			var log = MessageLog();

			return new VStack(spacing: 0f)
			{
				ChannelNameBar(topInset),

				// The message log (a LazyColumn) scrolls itself between the fixed bars; the
				// JumpToBottom FAB floats over its bottom-center (a ZStack overlay = Jetchat's
				// Messages() Box), fading in only while the log is scrolled away from the newest
				// message — the reactive-visibility + scroll-state capability (JumpToBottom.kt).
				new ZStack
				{
					log.FillHorizontal().FillVertical(),
					JumpToBottom(log)
						.HorizontalLayoutAlignment(LayoutAlignment.Center)
						.VerticalLayoutAlignment(LayoutAlignment.End)
						.Margin(bottom: 16),

					// The NotAvailable popup lives here as a zero-size overlay; it's a real Material
					// AlertDialog (its own window + scrim) that pops over everything when opened.
					NotAvailablePopup(),
				}.FillVertical(),

				UserInput(bottomInset, log),
			}.Background(Surface);
		}

		// ── Channel bar: ‹jetchat-logo⟩  ⟨#composers / 42 members⟩  ⌕  ⓘ ── (no bottom line; the
		// background tint separates it from the body). Mirrors a CenterAlignedTopAppBar: equal-flex
		// side zones screen-center the title; 16dp edge insets via the bar's own padding (leaf
		// padding is ignored by the layout engine, so spacing comes from the zones/margins). ──
		static View ChannelNameBar(double topInset) => new HStack(spacing: 0f)
		{
			// Left zone (flex): the jetchat logo at the start; the spacer balances the right zone.
			new HStack(spacing: 0f)
			{
				new Icon("jetchat").IconSize(28)   // multicolor brand logo (no tint → keeps its colors)
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
		}.Padding(new Thickness(16, topInset + 12, 16, 12)).Background(Surface);   // CenterAlignedTopAppBar = plain surface

		// A real Material Icon (ImageVector / SF Symbol), 24dp, tinted onSurfaceVariant, centered.
		static View BarIcon(string symbol) =>
			new Icon(symbol).Color(OnSurfaceVariant).IconSize(24)
				.VerticalLayoutAlignment(LayoutAlignment.Center);

		// ── Message log: a real virtualized LazyColumn (Comet ListView), exactly like the gold
		// standard's Messages() — each row template is materialized only when it scrolls into view. ──
		static ListView<Row> MessageLog() => new ListView<Row>(() => Rows)
		{
			ViewFor = r => r switch
			{
				HeaderRow h => DayHeader(h.Day),
				MsgRow m => MessageRow(m.M, m.TopOfGroup, m.BottomSpace),
				_ => new VStack(),
			},
		};

		// ── JumpToBottom (JumpToBottom.kt): an ExtendedFloatingActionButton — surface container,
		// primary content, 36dp tall, a down-arrow + "Jump to bottom". Hidden until the log scrolls
		// away from the newest message, then it fades in (reactive Opacity bound to the list's
		// ScrolledAway signal); tapping animates the log to the bottom (LazyListState scroller).
		// Styled as a Comet view here; a real ExtendedFloatingActionButton control is a follow-up. ──
		static View JumpToBottom(ListView list)
		{
			var fab = new HStack(spacing: 8f)
			{
				new Icon("arrow_down").Color(Primary).IconSize(18).VerticalLayoutAlignment(LayoutAlignment.Center),
				new Text("Jump to bottom").Color(Primary).LabelSmall().VerticalLayoutAlignment(LayoutAlignment.Center),
			}
				.Padding(new Thickness(16, 0, 16, 0)).Frame(height: 36)
				.Background(Surface).CornerRadius(18).Elevation(6)
				.Opacity(0)                                  // hidden until the log is scrolled away
				.OnTap(_ => list.ScrollToBottom());

			// Reactive show/hide: the backend node drives ScrolledAway from the LazyListState; mirror
			// it onto the FAB's opacity (UpdateBackendNode re-emits Opacity → ComposeNode .Alpha).
			list.ScrolledAway.PropertyChanged += (_, __) =>
				fab.Opacity(list.ScrolledAway.Peek() ? 1.0 : 0.0);

			return fab;
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
						new Image(isMe ? AvatarMe : AvatarOther).Frame(width: 36, height: 36).CornerRadius(18).FlexShrink(0),
					}
						.Frame(width: 42, height: 42)
						.FlexShrink(0)   // never compress the avatar's width when the row's text is wide
						.Padding(new Thickness(3))
						.Background(Surface)
						.CornerRadius(21)
						.Border(1.5, isMe ? Primary : Tertiary)   // Conversation.kt: border(1.5.dp, borderColor)
						.Margin(left: 16, right: 16)
						.VerticalLayoutAlignment(LayoutAlignment.Start)   // stay 42×42, don't stretch to row height
				: new HStack().Frame(width: 74).FlexShrink(0);

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
			new Text(m.Author).Color(OnSurface).TitleMedium().AlignBaseline(),
			new Text(m.Timestamp).Color(OnSurfaceVariant).BodySmall().AlignBaseline(),
		}.Padding(new Thickness(0, 0, 0, 4));

		// The iconic chat bubble: RoundedCornerShape(4,20,20,20); primary/white for me,
		// surfaceVariant/onSurface for others. Left-aligned and hugging its content (it grows to
		// the column width only when the text wraps).
		static View Bubble(string content, bool isMe) => new VStack(spacing: 0f)
		{
			new FormattedText(FormatMessage(content, isMe)).BodyLarge(),
		}
			.AsSurface()   // the gold standard draws the bubble with Surface(color, shape)
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
			.AsSurface()
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
		// full-width text field bound two-way to InputText. Row 2: the blue action icons spread
		// evenly to a Send button that reacts to whether there's text (UserInput.kt). ──
		static View UserInput(double bottomInset, ListView log)
		{
			// The Send button: empty → a bordered, grey OutlinedButton (the gold's disabled Send);
			// once there's text → a filled primary Button. The style is flipped reactively from the
			// InputText signal (mirrors `enabled = sendMessageEnabled`).
			var send = new Button("Send", () => Send(log))
				.LabelLarge().CornerRadius(18).Frame(height: 36)
				.VerticalLayoutAlignment(LayoutAlignment.Center);

			void Restyle()
			{
				if (string.IsNullOrWhiteSpace(InputText.Peek()))
					send.Outlined(true).Color(Disabled);     // empty → bordered, grey (disabled look)
				else
					send.Outlined(false).Color(OnPrimary);   // text present → filled primary
			}
			Restyle();
			InputText.PropertyChanged += (_, __) => Restyle();

			return new VStack(spacing: 0f)
			{
				SignalExtensions.TextField(InputText, "Message #composers")
					.Borderless().Color(OnSurface)
					.Padding(new Thickness(20, 14, 20, 10)),
				new HStack(spacing: 0f)
				{
					InputIcon("mood"), Spacer(),
					// The DM selector → "Functionality not available" popup (InputSelector.DM in the gold).
					InputIcon("at", () => NotAvailableOpen.Value = true), Spacer(), InputIcon("photo"),
					Spacer(), InputIcon("place"), Spacer(), InputIcon("video"), Spacer(),
					send,
				}.Padding(new Thickness(16, 4, 16, 12 + bottomInset)),
			}.Background(BarSurface);
		}

		// Append the composed text as a "me" message, clear the field, and scroll to the newest
		// (the C1 LazyListState scroller). Mirrors UserInput.kt onMessageSent + resetScroll.
		static void Send(ListView log)
		{
			var text = InputText.Peek();
			if (string.IsNullOrWhiteSpace(text))
				return;

			bool topOfGroup = Rows.Count == 0 || Rows[Rows.Count - 1] is not MsgRow last || last.M.Author != "me";
			Rows.Add(new MsgRow(new Msg("me", text.Trim(), System.DateTime.Now.ToString("h:mm tt")), topOfGroup, 8));

			InputText.Value = string.Empty;   // two-way binding clears the field
			log.ReloadData();                 // recompose the LazyColumn against the new row
			log.ScrollToBottom();             // animate to the newest message
		}

		static View InputIcon(string symbol, System.Action? onTap = null)
		{
			var icon = new Icon(symbol).Color(Secondary).IconSize(24)
				.VerticalLayoutAlignment(LayoutAlignment.Center);
			return onTap is null ? icon : icon.OnTap(_ => onTap());
		}

		// ── NotAvailablePopup (UiExtras.kt FunctionalityNotAvailablePopup): a real Material
		// AlertDialog — bodyMedium text + a "CLOSE" confirm button — shown when the DM selector is
		// picked. The CLOSE button and the scrim/back both clear the open signal. ──
		static View NotAvailablePopup() => new AlertDialog(
			NotAvailableOpen,
			text: new Text("Functionality not available \U0001F648").Color(OnSurface).BodyMedium(),
			confirmButton: new Button("CLOSE", () => NotAvailableOpen.Value = false).Color(Primary).LabelLarge());

		static View Spacer() => new HStack().FlexGrow(1);

		// A 1dp hairline in the divider color (used for the day header rule).
		static View HLine() => new HStack().Frame(height: 1).Background(Divider);
	}
}
