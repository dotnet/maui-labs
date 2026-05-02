using System;
using System.Collections.Generic;

using System.Text;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

/*
 
import SwiftUI

struct ContentView: View {
    var body: some View {
        Text("Hello SwiftUI!")
    }
}

 */

namespace Comet.Samples.Comparisons
{
	public class CometRideState
	{
		public int Rides { get; set; }
		public string CometTrain => "*".Repeat(Rides);
	}

	public class RideSample : Component<CometRideState>
	{
		public override View Render()
			=> VStack(
				Text($"({State.Rides}) rides taken:{State.CometTrain}")
					.Frame(width:300)
					.LineBreakMode(LineBreakMode.CharacterWrap),

				Button("Ride the Comet!", () => {
					SetState(s => s.Rides++);
				})
					.Frame(height:44)
					.Margin(8)
					.Color(Colors.White)
					.Background(Colors.Green)
				.RoundedBorder(color:Colors.Blue)
				.Shadow(Colors.Grey,4,2,2)
			);
	}
}

