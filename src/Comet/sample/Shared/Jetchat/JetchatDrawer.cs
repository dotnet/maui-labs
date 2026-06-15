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
	/// logo header, a "Chats" section (each chat a 56dp pill with the leading logo + a
	/// primaryContainer highlight when selected), a "Recent Profiles" section with avatars
	/// ("Ali Conors (you)"), and a "Settings" section. Rendered inside the real
	/// <see cref="Drawer"/> (Compose <c>ModalNavigationDrawer</c> / SwiftUI sliding panel).
	/// </summary>
	static class JetchatDrawer
	{
		public static View Content(double topInset, System.Action<string>? onProfile = null) => new VStack(spacing: 0f)
		{
			// DrawerHeader: the jetchat brand logo (multicolor Image).
			new HStack(spacing: 0f)
			{
				new Icon("jetchat").IconSize(36),
			}.Padding(new Thickness(20, topInset + 16, 16, 16)),

			// DrawerItemHeader("Chats") + 56dp ChatItem pills (leading logo + label, no '#').
			SectionTitle("Chats"),
			ChatItem("composers", selected: true),
			ChatItem("droidcon-nyc", selected: false),

			DividerItem(),
			SectionTitle("Recent Profiles"),
			ProfileItem("Ali Conors (you)", "Ali Conors", C.AvatarMe, onProfile),
			ProfileItem("Taylor Brooks", "Taylor Brooks", C.AvatarOther, onProfile),

			DividerItem(),
			SectionTitle("Settings"),
			SettingItem("Add Widget to Home Page"),
		}.Background(C.Surface);

		// DrawerItemHeader: bodySmall / onSurfaceVariant, horizontal 28dp.
		static View SectionTitle(string text) =>
			new Text(text).Color(C.OnSurfaceVariant).BodySmall()
				.Padding(new Thickness(28, 12, 16, 8));

		// ChatItem: a 56dp stadium pill — leading jetchat logo + label (bodyMedium); the selected
		// one fills with the (dynamic) primaryContainer and tints its content primary.
		static View ChatItem(string name, bool selected) => new HStack(spacing: 0f)
		{
			new Icon("jetchat").Color(selected ? C.Primary : C.OnSurfaceVariant).IconSize(24).FlexShrink(0)
				.Margin(left: 16, top: 16, bottom: 16).VerticalLayoutAlignment(LayoutAlignment.Center),
			new Text(name).Color(selected ? C.Primary : C.OnSurface).BodyMedium()
				.VerticalLayoutAlignment(LayoutAlignment.Center).Margin(left: 12),
		}
			.Frame(height: 56)
			.Background(selected ? JetchatTheme.PrimaryContainer : Colors.Transparent)
			.CornerRadius(28)
			.Margin(new Thickness(12, 0, 12, 0));

		// ProfileItem: 56dp pill — 24dp avatar + display name (bodyMedium). The display text may carry
		// a "(you)" suffix while the tap navigates by the bare profile name.
		static View ProfileItem(string display, string profileName, string avatar, System.Action<string>? onProfile) => new HStack(spacing: 0f)
		{
			new Image(avatar).Frame(width: 24, height: 24).CornerRadius(12).FlexShrink(0)
				.Margin(left: 16, top: 16, bottom: 16).VerticalLayoutAlignment(LayoutAlignment.Center),
			new Text(display).Color(C.OnSurface).BodyMedium()
				.VerticalLayoutAlignment(LayoutAlignment.Center).Margin(left: 12),
		}
			.Frame(height: 56).CornerRadius(28).Margin(new Thickness(12, 0, 12, 0))
			.OnTap(_ => onProfile?.Invoke(profileName));

		// A settings row (WidgetDiscoverability): bodyLarge, horizontal 28dp.
		static View SettingItem(string text) =>
			new Text(text).Color(C.OnSurface).BodyLarge().Padding(new Thickness(28, 16, 16, 16));

		// DividerItem: a hairline between sections, inset 28dp.
		static View DividerItem() => new HStack().Frame(height: 1).Background(C.Divider)
			.Margin(new Thickness(28, 8, 28, 8));
	}
}
