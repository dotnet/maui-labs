#nullable enable
using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Jetchat.JetchatTheme;
using C = CometSamples.Jetchat.JetchatConversation;

namespace CometSamples.Jetchat
{
	/// <summary>
	/// The Jetchat profile detail screen — the C# port of <c>profile/Profile.kt</c>: a circular
	/// avatar, the name (headlineSmall) + position (bodyLarge / onSurfaceVariant), a column of
	/// <c>ProfileProperty</c> rows (each preceded by a divider; the Twitter value is a primary-colored
	/// link), and a bottom-right tertiaryContainer FAB ("Message" for others, "Edit Profile" for me).
	/// Pushed onto the <see cref="NavigationView"/> stack by a drawer profile tap; the FAB / back arrow pop.
	/// </summary>
	static class JetchatProfile
	{
		// ProfileScreenState (data/FakeData.kt): the "me" profile and the single colleague profile.
		// PhotoAspect = the photo's width/height — the gold sizes the header image fillMaxWidth and lets
		// CircleShape clip it, so a square photo (ali 432x431) renders a CIRCLE and a landscape photo
		// (someone_else 640x427) a wide oval. Drives the box shape via AspectRatio (no intrinsic image size).
		sealed record Data(string Photo, double PhotoAspect, string Name, string Status, string DisplayName,
			string Position, string Twitter, string TimeZone, bool IsMe);

		static Data For(string name) => name == "Ali Conors"
			? new Data(C.AvatarMe, 432.0 / 431.0, "Ali Conors", "Online", "aliconors",
				"Senior Android Dev at Yearin\nGoogle Developer Expert", "twitter.com/aliconors", "In your timezone", true)
			: new Data(C.AvatarOther, 640.0 / 427.0, "Taylor Brooks", "Away", "taylor",
				"Senior Android Dev at Openlane", "twitter.com/taylorbrookscodes",
				"12:25 AM local time (Eastern Daylight Time)", false);

		public static View Screen(string name, double topInset, double bottomInset, Action onBack)
		{
			var d = For(name);

			// The gold standard is a Box: a full-bleed scrolling Column with a FloatingActionButton
			// aligned BottomEnd, floating over the content. A ZStack reproduces that — the scroll is
			// the base layer (fills), the FAB the overlay (pinned bottom-right, overlapping the scroll).
			// The scroll's AtTop signal drives the FAB: extended at top (scrollState.value == 0),
			// contracted when scrolled away — matching the gold's derivedStateOf { scrollState.value == 0 }.
			// Profile photo (ProfileHeader.kt): fillMaxWidth − 16dp each side, height from the photo's
			// aspect (AspectRatio), ContentScale.Crop, clipped CircleShape. A square photo (ali) → a
			// CIRCLE; a landscape photo (taylor) → a wide oval. Extracted so the parallax can translate it.
			var photo = new Image(d.Photo).FillHorizontal().AspectRatio(d.PhotoAspect).FlexShrink(0)
				.CornerRadius(1000).Margin(left: 16, top: 8, right: 16, bottom: 8);

			var scroll = new ScrollView
			{
				new VStack(spacing: 0f)
				{
					photo,

					// Name + position (NameAndPosition): each text baseline-anchored — the name's
					// first baseline 32dp from its top, the position's 24dp, +20dp below.
					new VStack(spacing: 0f)
					{
						new Text(d.Name).Color(T.OnSurface).HeadlineSmall().BaselineHeight(32),
						new Text(d.Position).Color(T.OnSurfaceVariant).BodyLarge()
							.BaselineHeight(24).Margin(bottom: 20),
					}.Padding(new Thickness(16, 8, 16, 0)),

					// Property rows, each preceded by a divider; Twitter is a primary-colored link.
					Property("Display name", d.DisplayName, isLink: false),
					Property("Status", d.Status, isLink: false),
					Property("Twitter", d.Twitter, isLink: true),
					Property("Timezone", d.TimeZone, isLink: false),
				},
			}.FillVertical();

			// NOTE: the gold ProfileHeader parallaxes the photo via padding(top = scrollState.value / 2),
			// which GROWS the photo's slot so the content below stays clear. The infrastructure for it is
			// in place — ScrollView.ScrollOffset (a continuous Dp signal the scroll node marshals) plus
			// general TranslationY transform support on the nodes — but a faithful (no-overlap) parallax
			// needs the layout-grow approach (a per-frame reflow), deferred to avoid scroll jank on the
			// profile's baseline-measured text. A pure TranslationY transform makes the photo overlap the
			// rows below, so it's not wired here.

			// FAB is extended while at top, contracted when scrolled (gold: derivedStateOf { scrollState.value == 0 }).
			var fab = ProfileFab(d.IsMe ? "Edit Profile" : "Message", d.IsMe ? "create" : "chat", onBack)
				.ExtendedWhen(scroll.AtTop);

			return new ZStack
			{
				new VStack(spacing: 0f)
				{
					// App-bar nav icon = the jetchat logo (JetchatAppBar.kt), which opens the drawer —
					// NOT a back button. The drawer wraps the whole nav stack, so it slides over the
					// profile just like in the original app.
					new HStack(spacing: 0f)
					{
						new Icon("jetchat").IconSize(32)
							.Margin(left: 16, top: (float)(topInset + 12))
							.OnTap(_ => C.DrawerOpen.Value = true),
						new HStack().FlexGrow(1),
					}.Background(T.Surface).Padding(new Thickness(0, 0, 0, 8)),

					scroll,
				}.FillHorizontal().FillVertical().Background(T.Surface),

				// The real Material ExtendedFloatingActionButton, floating bottom-right over the scroll.
				fab
					.HorizontalLayoutAlignment(LayoutAlignment.End)
					.VerticalLayoutAlignment(LayoutAlignment.End)
					.Margin(right: 16, bottom: (float)(24 + bottomInset)),
			};
		}

		// ProfileProperty: a divider, then the label and value, each baseline-anchored 24dp from the
		// top of its box (Jetchat's baselineHeight). Column padding: 16 start/end, 16 bottom.
		static View Property(string label, string value, bool isLink) => new VStack(spacing: 0f)
		{
			Divider(),
			new Text(label).Color(T.OnSurfaceVariant).BodySmall().BaselineHeight(24),
			new Text(value).Color(isLink ? T.Primary : T.OnSurface).BodyLarge().BaselineHeight(24),
		}.Padding(new Thickness(16, 0, 16, 16));

		static View Divider() => new HStack().Frame(height: 1).Background(T.Divider);

		// The real Material 3 FloatingActionButton (Profile.kt ProfileFab): tertiaryContainer
		// container, an icon + label, height 48. The icon/label carry no colour so they inherit the
		// FAB's onTertiaryContainer content colour. Starts extended (at-top); ExtendedWhen(scroll.AtTop)
		// is wired by the caller to contract when scrolled — matching AnimatingFabContent in the gold.
		static Comet.Fab ProfileFab(string text, string icon, Action onTap) => new Comet.Fab(
			icon: new Icon(icon).IconSize(24).Color(T.OnTertiaryContainer),
			label: new Text(text).LabelLarge().Color(T.OnTertiaryContainer),
			onClick: onTap,
			height: 48,
			containerColor: T.TertiaryContainer,
			contentColor: T.OnTertiaryContainer,
			extended: true);
	}
}
