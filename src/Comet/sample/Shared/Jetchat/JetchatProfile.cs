#nullable enable
using System;
using Comet;
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
		sealed record Data(string Photo, string Name, string Status, string DisplayName,
			string Position, string Twitter, string TimeZone, bool IsMe);

		static Data For(string name) => name == "Ali Conors"
			? new Data(C.AvatarMe, "Ali Conors", "Online", "aliconors",
				"Senior Android Dev at Yearin\nGoogle Developer Expert", "twitter.com/aliconors", "In your timezone", true)
			: new Data(C.AvatarOther, "Taylor Brooks", "Away", "taylor",
				"Senior Android Dev at Openlane", "twitter.com/taylorbrookscodes",
				"12:25 AM local time (Eastern Daylight Time)", false);

		public static View Screen(string name, double topInset, double bottomInset, Action onBack)
		{
			var d = For(name);

			// The gold standard is a Box: a full-bleed scrolling Column with a FloatingActionButton
			// aligned BottomEnd, floating over the content. A ZStack reproduces that — the scroll is
			// the base layer (fills), the FAB the overlay (pinned bottom-right, overlapping the scroll).
			return new ZStack
			{
				new VStack(spacing: 0f)
				{
					// Back arrow (the app bar's navigation icon).
					new HStack(spacing: 0f)
					{
						new Icon("back").Color(T.OnSurface).IconSize(24)
							.Margin(left: 8, top: (float)(topInset + 8)).OnTap(_ => onBack()),
						new HStack().FlexGrow(1),
					}.Background(T.Surface).Padding(new Thickness(0, 0, 0, 8)),

					new ScrollView
					{
						new VStack(spacing: 0f)
						{
							// Circular avatar: fillMaxWidth − 16dp each side, square (AspectRatio 1),
							// clipped to a circle (a corner radius past 50% clamps to a circle in Compose).
							new Image(d.Photo).FillHorizontal().AspectRatio(1).FlexShrink(0)
								.CornerRadius(1000).Margin(left: 16, top: 8, right: 16, bottom: 8),

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
					}.FillVertical(),
				}.FillHorizontal().FillVertical().Background(T.Surface),

				// The FloatingActionButton, floating bottom-right over the scroll (the ZStack overlay).
				Fab(d.IsMe ? "Edit Profile" : "Message", d.IsMe ? "create" : "chat", onBack)
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

		// FloatingActionButton: a tertiaryContainer pill with an icon + label, popping the stack.
		static View Fab(string text, string icon, Action onTap) => new HStack(spacing: 12f)
		{
			new Icon(icon).Color(T.OnTertiaryContainer).IconSize(20).VerticalLayoutAlignment(LayoutAlignment.Center),
			new Text(text).Color(T.OnTertiaryContainer).LabelLarge().VerticalLayoutAlignment(LayoutAlignment.Center),
		}
			.Padding(new Thickness(20, 0, 20, 0)).Frame(height: 48)
			.Background(T.TertiaryContainer).CornerRadius(16).OnTap(_ => onTap());
	}
}
