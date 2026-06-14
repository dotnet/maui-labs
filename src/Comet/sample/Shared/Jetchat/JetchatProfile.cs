#nullable enable
using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using C = CometSamples.Jetchat.JetchatConversation;

namespace CometSamples.Jetchat
{
	/// <summary>
	/// The Jetchat profile detail screen (the C# port of <c>ProfileScreen.kt</c>): a back bar, a
	/// full-width hero photo, the name + position, info sections, and a Message button. It's a real
	/// navigation destination — pushed onto the <see cref="NavigationView"/> stack by a drawer
	/// profile tap and popped by the back arrow / Message button.
	/// </summary>
	static class JetchatProfile
	{
		public static View Screen(string name, double topInset, double bottomInset, Action onBack)
		{
			bool isMe = name == "Ali Conors";
			var avatar = isMe ? C.AvatarMe : C.AvatarOther;

			return new VStack(spacing: 0f)
			{
				// Top bar with a back arrow that pops the navigation stack.
				new HStack(spacing: 0f)
				{
					new Icon("back").Color(C.OnSurface).IconSize(24)
						.Padding(new Thickness(12, topInset + 8, 12, 8))
						.OnTap(_ => onBack()),
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
							new Button("Message", () => onBack()),
						}.Padding(new Thickness(24, 16, 24, 24 + bottomInset)),
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
