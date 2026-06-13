using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.OS;
using Comet;
using Comet.Platform.Compose;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace CometComposeProbe
{
	/// <summary>
	/// Reproduces a Jetchat-style conversation screen (a C# port of Google's Compose
	/// "Jetchat" sample) on the Comet node backend: a fixed top bar over a virtualized,
	/// Yoga-laid-out message list. Every row is laid out by the shared C# Yoga engine
	/// (avatar + author + wrapping body), so it renders identically to the iOS/SwiftUI
	/// backend — no MAUI handlers in the path.
	/// </summary>
	[Activity(Label = "Comet+Compose", MainLauncher = true)]
	public class MainActivity : AndroidX.Activity.ComponentActivity
	{
		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			ActionBar?.Hide();

			ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

			var root = BuildUi();

			var backend = new ComposeBackendRoot(new EmptyServiceProvider()) { UseYogaLayout = true };
			var composeView = backend.CreateView(this, root);
			SetContentView(composeView);
		}

		sealed record Message(string Author, string Body, string AvatarSeed);

		static readonly List<Message> Conversation = new()
		{
			new("Taylor Brooks", "Morning everyone! Did you catch the new layout engine demo yesterday?", "taylor"),
			new("Ada Lovelace", "I did — the whole thing reflows from one flexbox pass now, shared across iOS and Android. Pretty wild.", "ada"),
			new("John Glenn", "So the same Comet tree lands pixel-identical on both backends? No per-platform tweaking?", "john"),
			new("Ada Lovelace", "That's the idea. Text wraps, avatars size, rows grow — all computed once in C#.", "ada"),
			new("Taylor Brooks", "Ship it. 🚀", "taylor"),
			new("Grace Hopper", "Let's make sure the long messages still wrap cleanly though — this one is intentionally a good deal longer so we can watch it spill onto several lines inside a virtualized row and confirm the row height grows to fit.", "grace"),
			new("John Glenn", "Confirmed on my Pixel — looks great.", "john"),
			new("Ada Lovelace", "And the list is genuinely lazy: rows only materialize as they scroll in.", "ada"),
			new("Taylor Brooks", "Perfect. Same screen, two platforms, one layout pass.", "taylor"),
			new("Grace Hopper", "That's the dream. Good work today, team.", "grace"),
		};

		View BuildUi() => new VStack(spacing: 0f)
		{
			// Top app bar
			new HStack(spacing: 12f)
			{
				new Text("#composers").Color(Colors.White),
			}.Padding(new Thickness(16, 44, 16, 16)).Background(Color.FromArgb("#6750A4")), // top inset clears the status bar

			// Scrolling, Yoga-laid-out message list filling the rest of the screen.
			new ListView<Message>(() => Conversation)
			{
				ViewFor = MessageRow,
			}.FillVertical(),
		}.Background(Color.FromArgb("#F2EFF7")); // tonal page behind the cards

		// Each message is a Material card: a rounded, raised white surface (corner radius +
		// elevation), inset from the page by the outer container's padding (the inter-card gap).
		static View MessageRow(Message m) => new VStack(spacing: 0f)
		{
			new HStack(spacing: 12f)
			{
				new Image($"https://picsum.photos/seed/{m.AvatarSeed}/80").Frame(width: 42, height: 42).CornerRadius(21),
				new VStack(spacing: 2f)
				{
					new Text(m.Author).Color(Color.FromArgb("#1C1B1F")),
					new Text(m.Body).Color(Color.FromArgb("#49454F")),
				},
			}.Padding(new Thickness(12)).Background(Colors.White).CornerRadius(16).Elevation(2),
		}.Padding(new Thickness(12, 6, 12, 6));

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
