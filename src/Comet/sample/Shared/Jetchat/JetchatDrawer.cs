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
		public static View Content(double topInset, Comet.Reactive.Signal<string> selected,
			System.Action<string> onChannel, System.Action<string> onProfile, System.Action onSettings) => new VStack(spacing: 0f)
		{
			// DrawerHeader (JetchatDrawer.kt): Row(padding 16){ JetchatIcon(24dp) + jetchat_logo wordmark }.
			// Android renders the gold two-layer tinted mark (ic_jetchat_back/front) + the jetchat_logo
			// wordmark image (87x24). iOS's bundle has only jetchat.png (the square mark) — the back/front
			// glyphs and the wordmark vector aren't there — so it shows the working jetchat mark + a text
			// wordmark. (Drop in iOS jetchat_back/front/jetchat_logo PNGs to make both platforms identical.)
			new HStack(spacing: 0f)
			{
				System.OperatingSystem.IsAndroid()
					? (View)C.JetchatIcon(24).VerticalLayoutAlignment(LayoutAlignment.Center)
					: new Icon("jetchat").IconSize(24).VerticalLayoutAlignment(LayoutAlignment.Center),
				System.OperatingSystem.IsAndroid()
					? (View)new Image("jetchat_logo").Frame(width: 87, height: 24).FlexShrink(0)
						.Margin(left: 8).VerticalLayoutAlignment(LayoutAlignment.Center)
					: new Text("jetchat").HeadlineSmall().Color(C.OnSurface)
						.Margin(left: 8).VerticalLayoutAlignment(LayoutAlignment.Center),
			}.Padding(new Thickness(16, topInset + 16, 16, 16)),

			// Edge-to-edge divider under the header (the gold's first DividerItem() has no inset).
			FullDivider(),

			// DrawerItemHeader("Chats") + 56dp ChatItem pills (leading logo + label, no '#'). Tapping
			// a channel returns to the messages view; the current destination is highlighted.
			SectionTitle("Chats"),
			ChatItem("composers", selected, onChannel),
			ChatItem("droidcon-nyc", selected, onChannel),

			InsetDivider(),
			SectionTitle("Recent Profiles"),
			ProfileItem("Ali Conors (you)", "Ali Conors", C.AvatarMe, selected, onProfile),
			ProfileItem("Taylor Brooks", "Taylor Brooks", C.AvatarOther, selected, onProfile),

			InsetDivider(),
			SectionTitle("Settings"),
			SettingItem("Add Widget to Home Page", onSettings),
		}.Background(C.Surface);

		// DrawerItemHeader: a 52dp row, text vertically centred, bodySmall / onSurfaceVariant, start
		// 28dp — so the header text left-aligns with the menu rows' icon column (12 row + 16 icon = 28).
		static View SectionTitle(string text) =>
			new HStack(spacing: 0f)
			{
				new Text(text).Color(C.OnSurfaceVariant).BodySmall()
					.VerticalLayoutAlignment(LayoutAlignment.Center),
			}.Frame(height: 52).Padding(new Thickness(28, 0, 16, 0));

		// ChatItem: a 56dp stadium pill — leading jetchat logo + label (bodyMedium). The pill, logo
		// tint, and label colour track the <paramref name="selected"/> destination signal reactively
		// (the selected one fills with the dynamic primaryContainer and tints its content primary);
		// tapping it routes back to the messages view via <paramref name="onChannel"/>.
		static View ChatItem(string name, Comet.Reactive.Signal<string> selected, System.Action<string> onChannel)
		{
			var icon = new Icon("jetchat").IconSize(24).FlexShrink(0)
				.Margin(left: 16, top: 16, bottom: 16).VerticalLayoutAlignment(LayoutAlignment.Center);
			var label = new Text(name).BodyMedium()
				.VerticalLayoutAlignment(LayoutAlignment.Center).Margin(left: 12);
			var pill = new HStack(spacing: 0f) { icon, label }
				.Frame(height: 56).CornerRadius(28).Margin(new Thickness(12, 0, 12, 0))
				.OnTap(_ => onChannel(name));

			void Apply()
			{
				bool sel = selected.Peek() == name;
				pill.Background(sel ? JetchatTheme.PrimaryContainer : Colors.Transparent);
				icon.Color(sel ? C.Primary : C.OnSurfaceVariant);
				label.Color(sel ? C.Primary : C.OnSurface);
			}
			Apply();
			selected.PropertyChanged += (_, __) => Apply();
			return pill;
		}

		// ProfileItem: 56dp pill — 24dp avatar + display name (bodyMedium). The display text may carry
		// a "(you)" suffix while the tap navigates by the bare profile name; the pill highlights when
		// its profile is the current destination.
		static View ProfileItem(string display, string profileName, string avatar,
			Comet.Reactive.Signal<string> selected, System.Action<string> onProfile)
		{
			var label = new Text(display).BodyMedium()
				.VerticalLayoutAlignment(LayoutAlignment.Center).Margin(left: 12);
			var pill = new HStack(spacing: 0f)
			{
				new Image(avatar).Frame(width: 24, height: 24).CornerRadius(12).FlexShrink(0)
					.Margin(left: 16, top: 16, bottom: 16).VerticalLayoutAlignment(LayoutAlignment.Center),
				label,
			}
				.Frame(height: 56).CornerRadius(28).Margin(new Thickness(12, 0, 12, 0))
				.OnTap(_ => onProfile(profileName));

			void Apply()
			{
				bool sel = selected.Peek() == profileName;
				pill.Background(sel ? JetchatTheme.PrimaryContainer : Colors.Transparent);
				label.Color(sel ? C.Primary : C.OnSurface);
			}
			Apply();
			selected.PropertyChanged += (_, __) => Apply();
			return pill;
		}

		// WidgetDiscoverability: a 56dp clickable pill (no icon), bodyMedium label at start 12 — same
		// row shape as the chat/profile items. Tapping invokes the supplied action (the gold adds a
		// home-screen widget; on iOS that surfaces the "not available" popup).
		static View SettingItem(string text, System.Action onTap) =>
			new HStack(spacing: 0f)
			{
				new Text(text).Color(C.OnSurface).BodyMedium()
					.VerticalLayoutAlignment(LayoutAlignment.Center).Margin(left: 12),
			}
				.Frame(height: 56).CornerRadius(28).Margin(new Thickness(12, 0, 12, 0))
				.OnTap(_ => onTap());

		// DividerItem (onSurface @ 12%): edge-to-edge under the header, inset 28dp between sections.
		static View FullDivider() => new HStack().FillHorizontal().Frame(height: 1).Background(C.Divider);
		static View InsetDivider() => new HStack().FillHorizontal().Frame(height: 1).Background(C.Divider)
			.Margin(new Thickness(28, 0, 28, 0));
	}
}
