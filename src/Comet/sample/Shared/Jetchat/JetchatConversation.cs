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
		// ── Jetchat light color scheme (theme/Themes.kt + Color.kt) ── (internal: shared w/ drawer)
		internal static readonly Color Primary = Color.FromArgb("#1546F6");          // Blue40 — my bubble + my avatar ring
		internal static readonly Color OnPrimary = Colors.White;                     // my bubble text
		internal static readonly Color Surface = Color.FromArgb("#FBFDFD");          // Grey99 — page + bars
		internal static readonly Color OnSurface = Color.FromArgb("#191C1D");        // Grey10 — titles, others' bubble text
		internal static readonly Color SurfaceVariant = Color.FromArgb("#E2E1EC");   // BlueGrey90 — others' bubble
		internal static readonly Color OnSurfaceVariant = Color.FromArgb("#45464F"); // BlueGrey30 — subtitle, timestamps, icons
		internal static readonly Color Tertiary = Color.FromArgb("#7A5900");         // Yellow40 — others' avatar ring
		internal static readonly Color Divider = Color.FromArgb("#1F191C1D");        // onSurface @ 12%

		internal const string AssetBase = "https://raw.githubusercontent.com/android/compose-samples/main/Jetchat/app/src/main/res/drawable-nodpi/";
		internal const string AvatarMe = AssetBase + "ali.png";
		internal const string AvatarOther = AssetBase + "someone_else.jpg";
		const string Sticker = AssetBase + "sticker.png";

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

		// ── Channel bar: ‹nav›  ⟨#composers / Members, 42⟩  ⌕  ⓘ ──
		static View ChannelNameBar(double topInset) => new VStack(spacing: 0f)
		{
			new HStack(spacing: 0f)
			{
				BarIcon("menu").OnTap(_ => DrawerOpen.Value = true),   // open the nav drawer
				new VStack(spacing: 0f)
				{
					new Text("#composers").Color(OnSurface).FontSize(16).FontWeight(FontWeight.Medium)
						.HorizontalLayoutAlignment(LayoutAlignment.Center),
					new Text("Members, 42").Color(OnSurfaceVariant).FontSize(12)
						.HorizontalLayoutAlignment(LayoutAlignment.Center),
				}.FlexGrow(1),
				BarIcon("search"),
				BarIcon("info"),
			}.Padding(new Thickness(4, topInset + 6, 4, 6)),

			HLine(),
		}.Background(Surface);

		// A real Material Icon (ImageVector / SF Symbol), 24dp, tinted onSurfaceVariant.
		static View BarIcon(string symbol) =>
			new Icon(symbol).Color(OnSurfaceVariant).IconSize(24).Padding(new Thickness(12, 14, 12, 14));

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

			// Left gutter: a 42dp avatar (with a tonal ring) on the group's top message; a 74dp
			// spacer otherwise — so a run of messages by one author lines up under the avatar.
			View gutter = topOfGroup
				? new VStack(spacing: 0f)
					{
						new Image(isMe ? AvatarMe : AvatarOther)
							.Frame(width: 42, height: 42).CornerRadius(21)
							.Border(2, isMe ? Primary : Tertiary),   // author ring (primary / tertiary)
					}.Padding(new Thickness(16, 0, 16, 0))
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
			new Text(m.Author).Color(OnSurface).FontSize(16).FontWeight(FontWeight.Medium),
			new Text(m.Timestamp).Color(OnSurfaceVariant).FontSize(12),
		}.Padding(new Thickness(0, 0, 0, 4));

		// The iconic chat bubble: RoundedCornerShape(4,20,20,20); primary/white for me,
		// surfaceVariant/onSurface for others. Left-aligned and hugging its content (it grows to
		// the column width only when the text wraps).
		static View Bubble(string content, bool isMe) => new VStack(spacing: 0f)
		{
			new Text(content).Color(isMe ? OnPrimary : OnSurface).FontSize(16),
		}
			.Padding(new Thickness(16))
			.Background(isMe ? Primary : SurfaceVariant)
			.CornerRadius(4, 20, 20, 20)
			.HorizontalLayoutAlignment(LayoutAlignment.Start);

		static View StickerBubble(bool isMe) => new VStack(spacing: 0f)
		{
			new Image(Sticker).Frame(width: 160, height: 160),
		}
			.Background(isMe ? Primary : SurfaceVariant)
			.CornerRadius(4, 20, 20, 20)
			.HorizontalLayoutAlignment(LayoutAlignment.Start);

		static View DayHeader(string day) => new HStack(spacing: 0f)
		{
			HLine().FlexGrow(1),
			new Text(day).Color(OnSurfaceVariant).FontSize(11).FontWeight(FontWeight.Medium).Padding(new Thickness(16, 0, 16, 0)),
			HLine().FlexGrow(1),
		}.Padding(new Thickness(16, 8, 16, 8));

		// ── Bottom input bar ──
		static View UserInput(double bottomInset) => new VStack(spacing: 0f)
		{
			HLine(),
			new TextField(new Comet.Reactive.Signal<string>(string.Empty), "Message #composers")
				.Padding(new Thickness(16, 12, 16, 12)),
			new HStack(spacing: 20f)
			{
				InputIcon("mood"),     // emoji
				InputIcon("at"),       // dm
				InputIcon("photo"),    // photos
				InputIcon("place"),    // location
				InputIcon("video"),    // video call
				new HStack().FlexGrow(1),
				// A real Material Button (filled), not a styled label.
				new Button("Send", () => { }).VerticalLayoutAlignment(LayoutAlignment.Center),
			}.Padding(new Thickness(16, 0, 16, 12 + bottomInset)),
		}.Background(Surface);

		static View InputIcon(string symbol) => new Icon(symbol).Color(OnSurfaceVariant).IconSize(24);

		// A 1dp hairline in the divider color (used for the bar borders + day header rule).
		static View HLine() => new HStack().Frame(height: 1).Background(Divider);
	}
}
