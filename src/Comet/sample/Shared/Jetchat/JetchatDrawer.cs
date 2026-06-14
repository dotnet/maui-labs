#nullable enable
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using C = CometSamples.Jetchat.JetchatConversation;

namespace CometSamples.Jetchat
{
	/// <summary>
	/// The Jetchat navigation-drawer panel (the C# port of <c>JetchatDrawer.kt</c>): the jetchat
	/// wordmark, a "Channels" section with the selected <c>#composers</c> highlighted, and a
	/// "Recent Profiles" section with avatars. Rendered inside the real
	/// <see cref="Drawer"/> (Compose <c>ModalNavigationDrawer</c> / SwiftUI sliding panel).
	/// </summary>
	static class JetchatDrawer
	{
		static readonly Color SelectedBg = Color.FromArgb("#DDE1FF");  // Blue90 — selected item container

		public static View Content(double topInset, System.Action<string>? onProfile = null) => new VStack(spacing: 0f)
		{
			// Header
			new HStack(spacing: 12f)
			{
				new Icon("account").Color(C.Primary).IconSize(28),
				new Text("Jetchat").Color(C.Primary).TitleLarge()
					.VerticalLayoutAlignment(LayoutAlignment.Center),
			}.Padding(new Thickness(20, topInset + 12, 16, 16)),

			SectionTitle("Channels"),
			ChannelItem("composers", selected: true),
			ChannelItem("droidcon-nyc", selected: false),

			new HStack().Frame(height: 8),
			SectionTitle("Recent Profiles"),
			ProfileItem("Ali Conors", C.AvatarMe, onProfile),
			ProfileItem("Taylor Brooks", C.AvatarOther, onProfile),
		}.Background(C.Surface);

		static View SectionTitle(string text) =>
			new Text(text).Color(C.OnSurfaceVariant).LabelSmall()
				.Padding(new Thickness(28, 12, 16, 8));

		// "# channel" row; the selected one gets a rounded primary-container highlight.
		static View ChannelItem(string name, bool selected) => new HStack(spacing: 0f)
		{
			new Text("#").Color(selected ? C.Primary : C.OnSurfaceVariant).BodyLarge()
				.Padding(new Thickness(0, 0, 8, 0)),
			new Text(name).Color(selected ? C.Primary : C.OnSurface).BodyLarge()
				.VerticalLayoutAlignment(LayoutAlignment.Center),
		}
			.Padding(new Thickness(16, 12, 16, 12))
			.Background(selected ? SelectedBg : Colors.Transparent)
			.CornerRadius(selected ? 28 : 0)
			.Margin(new Thickness(12, 0, 12, 0));

		static View ProfileItem(string name, string avatar, System.Action<string>? onProfile) => new HStack(spacing: 12f)
		{
			new Image(avatar).Frame(width: 24, height: 24).CornerRadius(12),
			new Text(name).Color(C.OnSurface).BodyLarge()
				.VerticalLayoutAlignment(LayoutAlignment.Center),
		}.Padding(new Thickness(28, 10, 16, 10)).OnTap(_ => onProfile?.Invoke(name));
	}
}
