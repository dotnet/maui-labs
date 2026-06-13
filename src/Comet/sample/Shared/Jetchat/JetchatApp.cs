#nullable enable
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using C = CometSamples.Jetchat.JetchatConversation;

namespace CometSamples.Jetchat
{
	/// <summary>
	/// Root of the Jetchat sample: an MVU <see cref="Component"/> that swaps between the
	/// drawer-wrapped conversation and a profile detail screen (the C# port of Jetchat's
	/// NavActivity nav graph). Tapping a profile in the drawer pushes the profile;
	/// the back arrow pops it — a single reactive <see cref="SetState"/>.
	/// </summary>
	public sealed class JetchatApp : Component<JetchatApp.S>
	{
		public sealed class S { public string? Profile; }

		readonly double _topInset, _bottomInset;

		public JetchatApp(double topInset, double bottomInset)
		{
			_topInset = topInset;
			_bottomInset = bottomInset;
		}

		public override View Render() =>
			State.Profile is { } name
				? Profile(name)
				: new Drawer(C.DrawerOpen,
					JetchatDrawer.Content(_topInset, OpenProfile),
					C.ConversationView(_topInset, _bottomInset));

		void OpenProfile(string name)
		{
			C.DrawerOpen.Value = false;
			SetState(s => s.Profile = name);
		}

		// ── Profile detail (ProfileScreen.kt): back bar, big photo, name, position, sections ──
		View Profile(string name)
		{
			bool isMe = name == "Ali Conors";
			var avatar = isMe ? C.AvatarMe : C.AvatarOther;

			return new VStack(spacing: 0f)
			{
				// Top bar with a back arrow.
				new HStack(spacing: 0f)
				{
					new Icon("back").Color(C.OnSurface).IconSize(24)
						.Padding(new Thickness(12, _topInset + 8, 12, 8))
						.OnTap(_ => SetState(s => s.Profile = null)),
					new HStack().FlexGrow(1),
				}.Background(C.Surface),

				new ScrollView
				{
					new VStack(spacing: 0f)
					{
						new Image(avatar).Frame(height: 320),  // full-width hero photo
						new VStack(spacing: 6f)
						{
							new Text(name).Color(C.OnSurface).FontSize(24).FontWeight(FontWeight.Bold),
							new Text("Senior Android Dev at Google").Color(C.OnSurfaceVariant).FontSize(14),

							new HStack().Frame(height: 12),
							Section("Display name", name.Replace(" ", "").ToLowerInvariant()),
							Section("Status", isMe ? "Away" : "Online"),
							Section("Twitter", "@" + name.Replace(" ", "").ToLowerInvariant()),
							Section("Timezone", "In your timezone"),

							new HStack().Frame(height: 16),
							new Button("Message", () => SetState(s => s.Profile = null)),
						}.Padding(new Thickness(24, 16, 24, 24 + _bottomInset)),
					},
				}.FillVertical(),
			}.Background(C.Surface);
		}

		static View Section(string label, string value) => new VStack(spacing: 2f)
		{
			new Text(label).Color(C.OnSurfaceVariant).FontSize(12).FontWeight(FontWeight.Medium),
			new Text(value).Color(C.OnSurface).FontSize(16),
		}.Padding(new Thickness(0, 12, 0, 0));
	}
}
