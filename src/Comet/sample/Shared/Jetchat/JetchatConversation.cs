#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
		internal static Color InversePrimary => JetchatTheme.InversePrimary;
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

		// Display = [9 gold messages][~100 history]. Render history (older) first, then the gold's two
		// hardcoded day dividers ("20 Aug" + "Today") around the 9-message conversation — so the newest
		// real message ("Check it out!") is the LAST row and the list opens scrolled to it (Conversation.kt
		// reverseLayout + the index==size-1 / index==2 DayHeaders).
		const int GoldCount = 9;

		static List<Row> BuildRows()
		{
			var real = Display.GetRange(0, GoldCount);
			var history = Display.GetRange(GoldCount, Display.Count - GoldCount);

			var rows = new List<Row>();
			AddMessageRows(rows, history);                              // older history (top)
			rows.Add(new HeaderRow("20 Aug"));
			AddMessageRows(rows, real.GetRange(0, 3));                  // John 8:12, Taylor 8:10, Shangeeth 8:08
			rows.Add(new HeaderRow("Today"));
			AddMessageRows(rows, real.GetRange(3, real.Count - 3));     // me 8:03 … Check it out 8:07
			return rows;
		}

		// Append message rows for one contiguous segment, computing author-grouping within it (the
		// first row starts a new group — a header/segment boundary precedes it).
		static void AddMessageRows(List<Row> rows, IReadOnlyList<Msg> msgs)
		{
			for (int i = 0; i < msgs.Count; i++)
			{
				var m = msgs[i];
				bool topOfGroup = i == 0 || msgs[i - 1].Author != m.Author;             // avatar + name here
				bool bottomOfGroup = i == msgs.Count - 1 || msgs[i + 1].Author != m.Author;
				rows.Add(new MsgRow(m, topOfGroup, bottomOfGroup ? 8 : 4));
			}
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

		// The active input selector (gold UserInput.kt InputSelector enum). Drives the expandable panel
		// (emoji table / not-available pane — the F1 swap-panel capability) and the selected-icon
		// highlight. The indices match the enum so the SelectorPanel slot list lines up with the icons.
		const int SelNone = 0, SelMap = 1, SelDm = 2, SelEmoji = 3, SelPhone = 4, SelPicture = 5;
		static readonly Comet.Reactive.Signal<int> CurrentSelector = new(SelNone);

		// Voice-record indicator state (gold UserInput.kt RecordButton/RecordingIndicator). The live
		// mm:ss elapsed time shown while the mic is held, reactively bound to the indicator's Text. The
		// gold records no audio — RecordButton is a pure UI mock (it just toggles a flag) — and so do we;
		// this is the gesture + indicator reproduction. Android-only: the iOS mic stays static.
		static readonly Comet.Reactive.Signal<string> RecordDuration = new("00:00");

		/// <summary>The Jetchat sample. The drawer wraps the whole <see cref="NavigationView"/> stack
		/// (like the gold's <c>ModalNavigationDrawer</c> around the NavHost), so the jetchat logo opens
		/// it over ANY destination — the conversation or a profile. Tapping a drawer profile closes the
		/// drawer and <c>Navigate</c>s the profile detail onto the stack; the profile's Message FAB
		/// <c>Pop</c>s it.</summary>
		// The drawer's current destination (a channel key like "composers" or a profile name), so the
		// drawer highlights the active item; "composers" is the initial channel.
		static readonly Comet.Reactive.Signal<string> Selected = new("composers");
		static string _lastChannel = "composers";

		public static View Build(double topInset = 24, double bottomInset = 0)
		{
			var nav = new NavigationView();
			int depth = 0;   // profiles currently pushed above the conversation

			// A drawer channel tap: return to the messages view (pop any profiles) and mark it selected.
			void SelectChannel(string name)
			{
				Selected.Value = name;
				_lastChannel = name;
				while (depth > 0) { nav.Pop(); depth--; }
				DrawerOpen.Value = false;
			}
			// A drawer profile tap: mark it selected and push the profile detail (the Message FAB /
			// back restores the last channel as the selection).
			void OpenProfile(string profileName)
			{
				Selected.Value = profileName;
				DrawerOpen.Value = false;
				depth++;
				nav.Navigate(JetchatProfile.Screen(profileName, topInset, bottomInset,
					() => { nav.Pop(); depth--; Selected.Value = _lastChannel; }));
			}

			// A @mention tap (from the message formatter) resolves its handle to a profile and pushes it.
			NavigateToProfile = handle => { if (ProfileForHandle(handle) is { } n) OpenProfile(n); };

			nav.Content = ConversationView(topInset, bottomInset);
			// Settings → the gold adds a home-screen widget; iOS has no equivalent, so surface the
			// "functionality not available" popup (same as the DM selector).
			void OpenSettings() => NotAvailableOpen.Value = true;
			return new Drawer(DrawerOpen, JetchatDrawer.Content(topInset, Selected, SelectChannel, OpenProfile, OpenSettings), nav);
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
						.Margin(bottom: 32),

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
				BarIcon("search", () => NotAvailableOpen.Value = true).Margin(right: 20),
				BarIcon("info", () => NotAvailableOpen.Value = true),
			}.FlexGrow(1).FlexBasis(0),
		}.Padding(new Thickness(16, topInset + 12, 16, 12)).Background(Surface);   // CenterAlignedTopAppBar = plain surface

		// JetchatIcon (components/JetchatIcon.kt): the two-layer, theme-TINTED brand mark — ic_jetchat_back
		// (primaryContainer) under ic_jetchat_front (primary), stacked. Unlike the static multicolor
		// ic_jetchat, it tints with the Material scheme (so it tracks Material You / the brand seed). Both
		// layers are tinted Icons (non-multicolor → the tinted Icon path = the gold's colorFilter.tint).
		internal static View JetchatIcon(double size = 24) => new ZStack
		{
			new Icon("jetchat_back").Color(JetchatTheme.PrimaryContainer).IconSize(size),
			new Icon("jetchat_front").Color(Primary).IconSize(size),
		}.Frame(width: (float)size, height: (float)size).FlexShrink(0);

		// A real Material Icon (ImageVector / SF Symbol), 24dp, tinted onSurfaceVariant, centered.
		// Search/info tap the "functionality not available" popup (Conversation.kt clickable actions).
		static View BarIcon(string symbol, System.Action? onTap = null)
		{
			var icon = new Icon(symbol).Color(OnSurfaceVariant).IconSize(24)
				.VerticalLayoutAlignment(LayoutAlignment.Center);
			return onTap is null ? icon : icon.OnTap(_ => onTap());
		}

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

		// ── JumpToBottom (JumpToBottom.kt): a real Material ExtendedFloatingActionButton — surface
		// container, primary content, 36dp tall, a down-arrow + "Jump to bottom". Hidden until the
		// log scrolls away from the newest message (reactive Opacity bound to the list's ScrolledAway
		// signal); tapping animates the log to the bottom (LazyListState scroller). The icon/label
		// carry no colour so they inherit the FAB's primary content colour. The gold uses
		// ExtendedFloatingActionButton (always extended — never contracted); we match that here now
		// that the ExtendedFAB slot bug is fixed via the RenderDirect bridge path. ──
		static View JumpToBottom(ListView list)
		{
			var fab = new Comet.Fab(
				// Colour the icon + label explicitly (not just via the FAB's contentColor): Compose's
				// ExtendedFAB tints its slot content through LocalContentColor, but the SwiftUI FAB renders
				// the children directly, so without an explicit colour the iOS label rendered invisible.
				icon: new Icon("arrow_down").IconSize(18).Color(Primary),
				label: new Text("Jump to bottom").LabelSmall().Color(Primary),
				onClick: () => list.ScrollToBottom(),
				height: 36,
				containerColor: Surface,
				contentColor: Primary,
				extended: true)    // always extended — the gold's ExtendedFloatingActionButton never contracts
				.Opacity(0);       // hidden until the log is scrolled away from the newest message

			// Reactive show/hide: the backend node drives ScrolledAway from the LazyListState; mirror
			// it onto the FAB's opacity (ComposeFabNode keeps it composed but pushes it off-screen).
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

		// The C# port of Jetchat's messageFormatter (MessageFormatter.kt). One pass over the gold
		// symbolPattern: links + `code` + @mentions + *bold* + _italic_ + ~strike~. @mentions are
		// bold and tap to the author's profile; links tap to the browser (no underline — gold colors
		// only); `code` is monospace 12sp in a tonal box. The accent is inversePrimary on my (primary)
		// bubbles and primary on others; the code box is secondary(me)/surface(other) — all from source.
		static readonly Regex SymbolPattern = new(
			@"(https?://[^\s\t\n]+)|(`[^`]+`)|(@\w+)|(\*[\w]+\*)|(_[\w]+_)|(~[\w]+~)",
			RegexOptions.Compiled);

		static System.Collections.Generic.IReadOnlyList<TextRun> FormatMessage(string content, bool isMe)
		{
			var baseColor = isMe ? OnPrimary : OnSurface;
			var accent = isMe ? InversePrimary : Primary;     // gold: inversePrimary(me) / primary(other)
			var codeBg = isMe ? Secondary : Surface;          // gold: secondary(me) / surface(other)

			var runs = new System.Collections.Generic.List<TextRun>();
			int cursor = 0;
			foreach (Match m in SymbolPattern.Matches(content))
			{
				if (m.Index > cursor)
					runs.Add(new TextRun(content.Substring(cursor, m.Index - cursor), Color: baseColor));

				string tok = m.Value;
				switch (tok[0])
				{
					case '@':
						string handle = tok.Substring(1);
						runs.Add(new TextRun(tok, Color: accent, Bold: true,
							OnTap: () => NavigateToProfile?.Invoke(handle)));
						break;
					case '`':
						runs.Add(new TextRun(tok.Trim('`'), Color: baseColor, Monospace: true,
							FontSize: 12, Background: codeBg));
						break;
					case '*':
						runs.Add(new TextRun(tok.Trim('*'), Color: baseColor, Bold: true));
						break;
					case '_':
						runs.Add(new TextRun(tok.Trim('_'), Color: baseColor, Italic: true));
						break;
					case '~':
						runs.Add(new TextRun(tok.Trim('~'), Color: baseColor, Strikethrough: true));
						break;
					default:    // http(s) link
						runs.Add(new TextRun(tok, Color: accent, OnTap: () => OpenUrl?.Invoke(tok)));
						break;
				}
				cursor = m.Index + m.Length;
			}
			if (cursor < content.Length)
				runs.Add(new TextRun(content.Substring(cursor), Color: baseColor));
			return runs;
		}

		// Tap hooks, wired in Build(). A @mention resolves its handle to a profile and navigates;
		// a link opens the URL (the host wires a real browser-open). Both no-op until Build() runs.
		internal static System.Action<string>? NavigateToProfile;
		internal static System.Action<string>? OpenUrl;

		// The seed data's only mention is @aliconors; map the known handles to their profile names.
		static string? ProfileForHandle(string handle) => handle.ToLowerInvariant() switch
		{
			"aliconors" => "Ali Conors",
			"taylor" or "taylorbrookscodes" => "Taylor Brooks",
			_ => null,
		};

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
					send.Outlined(true).Color(Disabled).Background(Colors.Transparent);  // empty → bordered, grey
				else
					send.Outlined(false).Color(OnPrimary).Background(Primary);            // text → filled primary
			}
			Restyle();
			InputText.PropertyChanged += (_, __) => Restyle();

			// The expandable selector panel (gold UserInput.kt SelectorExpanded): one Surface that swaps
			// content by the active InputSelector. Slot indices match the enum; null slots collapse it.
			// EMOJI → the emoji table; MAP/PHONE/PICTURE → the 320dp "not available" pane; NONE/DM →
			// nothing (DM opens the separate not-available dialog). Opening a panel grows the footer and
			// shrinks the message list (the panel reports its height to the Yoga engine), exactly like
			// the gold's bottom-anchored Surface; a system back press collapses it.
			var panel = new SelectorPanel(CurrentSelector, new View?[]
			{
				null,                  // NONE
				NotAvailablePanel(),   // MAP
				null,                  // DM (separate dialog)
				EmojiPanel(),          // EMOJI
				NotAvailablePanel(),   // PHONE
				NotAvailablePanel(),   // PICTURE
			}).Background(JetchatTheme.SurfaceTinted8).FillHorizontal();

			// The borderless composer field — gold UserInputText. Send-on-keyboard: the IME action key is a
			// paper-plane "Send" that submits (gold KeyboardOptions(imeAction = Send)); focusing it closes
			// any open selector panel (gold onTextFieldFocused) so the keyboard never overlays it.
			var field = SignalExtensions.TextField(InputText, "Message #composers", completed: () => Send(log))
				.SendOnReturn()
				.OnFocused(() => { CurrentSelector.Value = SelNone; log.ScrollToBottom(); })
				.Borderless().Color(OnSurface).FillHorizontal()
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.Padding(new Thickness(20, 0, 8, 0));
			ComposerField = field;

			// The recording indicator that replaces the field while the mic is held (gold RecordingIndicator):
			// a red dot, the live mm:ss elapsed time, and a "Swipe to cancel" hint that fades as the drag
			// nears the cancel threshold. Hidden (Opacity 0) until a long-press starts recording. Both
			// backends honour Opacity and make a hidden node non-interactive, so the SAME swap works on iOS —
			// where the record gesture is dormant (press-and-hold is a Compose-only gesture) and the
			// indicator simply stays hidden over the field.
			var swipeHint = new Text("Swipe to cancel").BodyLarge().Color(OnSurfaceVariant)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.VerticalLayoutAlignment(LayoutAlignment.Center);
			var recordDot = new HStack().Frame(width: 10, height: 10).CornerRadius(5).Background(Colors.Red)
				.VerticalLayoutAlignment(LayoutAlignment.Center);
			var indicator = new HStack(spacing: 12f)
			{
				recordDot,
				new Text(RecordDuration).BodyLarge().Color(OnSurface)
					.VerticalLayoutAlignment(LayoutAlignment.Center),
				swipeHint.FillHorizontal(),
			}.FillHorizontal().Padding(new Thickness(20, 0, 8, 0)).Opacity(0);

			// The mic — gold RecordButton. A 48dp touch target (so the long-press lands easily) carrying the
			// press-and-hold voice-record gesture: long-press starts, drag tracks the swipe offset, release
			// finishes, swipe-left past the threshold cancels (gold detectDragGesturesAfterLongPress). On iOS
			// the SwiftUI backend has no record gesture wired, so this is dormant there.
			var micIcon = new Icon("mic").Color(OnSurfaceVariant).IconSize(24)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.VerticalLayoutAlignment(LayoutAlignment.Center);
			var mic = new ZStack { micIcon }
				.Frame(width: 48, height: 48).Margin(right: 8)
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.OnRecord(g => HandleRecord(g, field, indicator, swipeHint, micIcon, recordDot));

			return new VStack(spacing: 0f)
			{
				// Input row: a fixed 64dp row (the gold's UserInputText height). The field and the recording
				// indicator share the leading cell (one fades out as the other fades in); the mic trails.
				new HStack(spacing: 0f)
				{
					new ZStack { field, indicator }.FillHorizontal(),
					mic,
				}.Frame(height: 64),
				// Selector row (gold UserInputSelector): five toggle icons packed at the start, a flexible
				// gap, then Send. 72dp tall with 16dp bottom padding — height(72).padding(start=16, end=16,
				// bottom=16). Icon→selector: mood=EMOJI, at=DM, photo=PICTURE, place=MAP, video=PHONE.
				new HStack(spacing: 0f)
				{
					SelectorButton("mood", SelEmoji),
					SelectorButton("at", SelDm),
					SelectorButton("photo", SelPicture),
					SelectorButton("place", SelMap),
					SelectorButton("video", SelPhone),
					Spacer(),
					send,
				}.Frame(height: 72).Padding(new Thickness(16, 0, 16, 16)),
				panel,
				// Safe-area fill: extends BarSurface behind the system navigation bar / home indicator.
				new HStack().Frame(height: (float)bottomInset),
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

		// Drive the voice-record indicator from the press-and-hold gesture (gold RecordButton's
		// onStartRecording/onFinishRecording/onCancelRecording + RecordingIndicator). The gold records
		// nothing — RecordButton is a pure UI mock — so this only swaps the field for the indicator, runs
		// the elapsed timer, and fades the "Swipe to cancel" hint as the drag nears the threshold.
		static void HandleRecord(RecordGesture g, View field, View indicator, View swipeHint, View micIcon, View recordDot)
		{
			switch (g.Status)
			{
				case Comet.GestureStatus.Started:
					swipeHint.Opacity(1);
					field.Opacity(0);          // fade the field out …
					indicator.Opacity(1);      // … and the recording indicator in
					micIcon.Color(Primary);    // tint the mic active (gold animates the icon colour)
					// The gold's infiniteRepeatable alpha pulse on the red dot, driven by
					// Comet's own animation engine (Choreographer-ticked on the node backend).
					recordDot.Animate(v => v.Opacity(0.2), duration: 0.6,
						repeats: true, autoReverses: true, id: "recordPulse");
					StartRecordTimer();
					break;
				case Comet.GestureStatus.Running:
					// Fade the hint as the leftward swipe approaches the 200dp cancel threshold (gold alpha).
					double frac = System.Math.Min(1.0, System.Math.Abs(g.TotalX) / 200.0);
					swipeHint.Opacity(1.0 - frac);
					break;
				case Comet.GestureStatus.Completed:
				case Comet.GestureStatus.Canceled:
					StopRecordTimer();
					recordDot.AbortAnimation("recordPulse");
					recordDot.Opacity(1);
					field.Opacity(1);          // restore the field
					indicator.Opacity(0);
					micIcon.Color(OnSurfaceVariant);
					break;
			}
		}

		// The recording elapsed-time ticker (gold RecordingIndicator's LaunchedEffect { while(true) delay(1000) }).
		static System.Threading.Timer? _recordTimer;
		static int _recordSeconds;
		static void StartRecordTimer()
		{
			_recordSeconds = 0;
			RecordDuration.Value = "00:00";
			_recordTimer?.Dispose();
			_recordTimer = new System.Threading.Timer(_ =>
			{
				_recordSeconds++;
				RecordDuration.Value = $"{_recordSeconds / 60:00}:{_recordSeconds % 60:00}";
			}, null, 1000, 1000);
		}
		static void StopRecordTimer()
		{
			_recordTimer?.Dispose();
			_recordTimer = null;
		}

		// A selector icon (gold InputSelectorButton): a 48dp rounded touch target. When its selector is
		// active it fills with the footer's content colour (secondary) clipped to RoundedCornerShape(14)
		// and the glyph flips to the contrasting onSecondary tint; otherwise a plain onSurfaceVariant
		// glyph. The fill + tint flip reactively from the CurrentSelector signal (the same restyle
		// pattern the Send button uses). The icon is centred in the box by a ZStack.
		static View SelectorButton(string symbol, int selector)
		{
			var icon = new Icon(symbol).IconSize(24).Color(OnSurfaceVariant)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.VerticalLayoutAlignment(LayoutAlignment.Center);
			var box = new ZStack { icon }
				.Frame(width: 48, height: 48).CornerRadius(14)
				.OnTap(_ => OnSelector(selector));

			void Restyle()
			{
				bool sel = CurrentSelector.Peek() == selector;
				box.Background(sel ? Secondary : Colors.Transparent);
				icon.Color(sel ? JetchatTheme.OnSecondary : OnSurfaceVariant);
			}
			Restyle();
			CurrentSelector.PropertyChanged += (_, __) => Restyle();
			return box;
		}

		// Tap a selector icon. EMOJI/PICTURE/MAP/PHONE open that panel; DM closes any panel and shows the
		// not-available dialog (gold InputSelector.DM -> NotAvailablePopup). Matches the gold's
		// non-toggling onSelectorChange — the panel's BackHandler (or picking another selector) dismisses.
		static void OnSelector(int selector)
		{
			if (selector == SelDm)
			{
				CurrentSelector.Value = SelNone;
				NotAvailableOpen.Value = true;
				return;
			}
			// Tapping the already-open selector collapses it — a discoverable dismiss (the gold doesn't
			// toggle, but its back-press + tap-the-field dismisses aren't obvious on a gesture-nav device).
			CurrentSelector.Value = CurrentSelector.Peek() == selector ? SelNone : selector;
		}

		// ── SelectorExpanded panes (gold UserInput.kt) ───────────────────────────────────────────────

		// FunctionalityNotAvailablePanel: a 320dp pane with a vertically-centred title + subtitle, shown
		// for the MAP / PHONE / PICTURE selectors.
		static View NotAvailablePanel() => new VStack(spacing: 0f)
		{
			Spacer(),
			new Text("Functionality currently not available").Color(OnSurface).TitleMedium()
				.HorizontalLayoutAlignment(LayoutAlignment.Center),
			new Text("Grab a beverage and check back later!").Color(OnSurfaceVariant).BodyMedium()
				.HorizontalLayoutAlignment(LayoutAlignment.Center).Margin(top: 16),
			Spacer(),
		}.Frame(height: 320).FillHorizontal();

		// EmojiSelector: Emojis/Stickers tabs over a 4×10 emoji table. The Emojis tab is always shown
		// selected (the gold hardcodes selected=true); the Stickers tab pops the not-available dialog.
		static View EmojiPanel() => new VStack(spacing: 0f)
		{
			new HStack(spacing: 0f)
			{
				EmojiTab("Emojis", selected: true, onTap: null),
				EmojiTab("Stickers", selected: false, onTap: () => NotAvailableOpen.Value = true),
			}.Padding(new Thickness(8, 0, 8, 0)),
			EmojiTable(),
		}.Padding(new Thickness(8)).FillHorizontal();

		// ExtendedSelectorInnerButton: a real Material TextButton, titleSmall, that fills onSurface@8%
		// when selected (else transparent). FlexGrow gives the two tabs equal width.
		static View EmojiTab(string text, bool selected, System.Action? onTap)
		{
			var btn = new Button(text, () => onTap?.Invoke())
				.TextButton().TitleSmall().Color(OnSurface)
				.Frame(height: 36).FlexGrow(1).Margin(new Thickness(8)).CornerRadius(18);
			if (selected)
				btn.Background(OnSurface.WithAlpha(0.08f));   // gold: onSurface.copy(alpha = 0.08f)
			return btn;
		}

		// 4 rows × EMOJI_COLUMNS cells; each emoji is a centred, tappable Text (a real Compose Text +
		// clickable, like the gold's EmojiTable). FlexGrow gives the columns equal width.
		static View EmojiTable()
		{
			var col = new VStack(spacing: 0f).FillHorizontal();
			for (int x = 0; x < 4; x++)
			{
				var row = new HStack(spacing: 0f).FillHorizontal();
				for (int y = 0; y < EmojiColumns; y++)
				{
					string e = Emojis[x * EmojiColumns + y];
					row.Add(new ZStack { new Text(e).FontSize(18) }
						.Frame(height: 44).FlexGrow(1).FlexBasis(0)
						.OnTap(_ => AddEmoji(e)));
				}
				col.Add(row);
			}
			return col;
		}

		// The gold's insert-at-cursor (UserInput.kt addText): the emoji lands at the caret,
		// replacing any selection, and the caret moves past it. Falls back to appending on a
		// backend without caret tracking (the field's two-way signal updates either way).
		static TextField? ComposerField;
		static void AddEmoji(string emoji)
		{
			if (ComposerField is { } f)
				f.InsertAtCursor(emoji);
			else
				InputText.Value = InputText.Peek() + emoji;
		}

		const int EmojiColumns = 10;

		// The gold's first 40 emojis (emojis[0..39] from UserInput.kt), as full code points.
		static readonly string[] Emojis =
		{
			"\U0001F600", "\U0001F601", "\U0001F602", "\U0001F603", "\U0001F604", "\U0001F605", "\U0001F606", "\U0001F609", "\U0001F60A", "\U0001F60B",
			"\U0001F60E", "\U0001F60D", "\U0001F618", "\U0001F617", "\U0001F619", "\U0001F61A", "☺", "\U0001F642", "\U0001F917", "\U0001F607",
			"\U0001F913", "\U0001F914", "\U0001F610", "\U0001F611", "\U0001F636", "\U0001F644", "\U0001F60F", "\U0001F623", "\U0001F625", "\U0001F62E",
			"\U0001F910", "\U0001F62F", "\U0001F62A", "\U0001F62B", "\U0001F634", "\U0001F60C", "\U0001F61B", "\U0001F61C", "\U0001F61D", "\U0001F612",
		};

		// ── NotAvailablePopup (UiExtras.kt FunctionalityNotAvailablePopup): a real Material
		// AlertDialog — bodyMedium text + a "CLOSE" confirm button — shown when the DM selector is
		// picked. The CLOSE button and the scrim/back both clear the open signal. ──
		static View NotAvailablePopup() => new AlertDialog(
			NotAvailableOpen,
			text: new Text("Functionality not available \U0001F648").Color(OnSurface).BodyMedium(),
			confirmButton: new Button("CLOSE", () => NotAvailableOpen.Value = false).TextButton().Color(Primary).LabelLarge());

		static View Spacer() => new HStack().FlexGrow(1);

		// A 1dp hairline in the divider color (used for the day header rule).
		static View HLine() => new HStack().Frame(height: 1).Background(Divider);
	}
}
