using System;
using Comet.Samples.Models;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class ListViewDetails : Component
	{
		[Environment]
		readonly Song song;

		public override View Render() => VStack(
			Image(() => song.ArtworkUrl),
			Text(() => song.Title),
			Text(() => song.Artist),
			Text(() => song.Album)
		);
	}
}
