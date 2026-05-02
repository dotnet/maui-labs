using System;
using System.Collections.Generic;

using Comet.Samples.Models;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class ListViewSample2 : Component
	{
		//This should come from a database or something
		List<Song> Songs = new List<Song>
		{
			new Song
			{
				Title = "All the Small Things",
				Artist = "Blink-182",
				Album = "Dude Ranch",
				ArtworkUrl = "https://lh3.googleusercontent.com/9Ofo9ZHQODFvahjpq2ZVUUOog4v5J1c4Gw9qjTw-KADTQZ6sG98GA1732mZA165RBoyxfoMblA"
			},
			new Song
			{
				Title = "Monster",
				Artist = "Skillet",
				Album = "Awake",
				ArtworkUrl = "https://lh3.googleusercontent.com/uhjRXO19CiZbT46srdXSM-lQ8xCsurU-xaVg6lvJvNy8TisdjlaHrHsBwcWAzpu_vkKXAA9SdbA",
			}
		};

		public override View Render() => new ListView<Song>(Songs)
		{
			ViewFor = song => HStack(
				Image(song.ArtworkUrl)
					.Frame(44,44).Alignment(Alignment.Center)
					.Margin(left:10f)
					.ClipShape(new Circle()),
				VStack(LayoutAlignment.Start,
					Text(song.Title),
					Text(song.Artist),
					Text(song.Album)
				)
			).Alignment(Alignment.Leading),
			Header = VStack(
				Text("Songs")
			),
		}.OnSelected((song) => { Console.WriteLine("Song Selected"); });
	}
}
