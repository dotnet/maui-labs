using System;
using System.Collections.Generic;
using System.Text;
using static Comet.CometControls;


/*
 
import SwiftUI

struct CircleImage: View {
    var body: some View {
        Image("turtlerock")
            .clipShape(Circle())
            .overlay(
                Circle().stroke(Color.white, lineWidth: 4))
            .shadow(radius: 10)
    }
}

 */

namespace Comet.Samples.Comparisons
{
	public class Section4c : Component
	{
				public override View Render() => VStack(
				Image("turtlerock.jpg")
					.Shadow(radius: 10)
			);

	}
}
