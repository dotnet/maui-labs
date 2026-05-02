using System;
using System.Collections.Generic;
using System.Text;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class TextStylesSample : Component
	{
				public override View Render() => ScrollView(
			VStack(
				Text("H1").StyleAsH1(),
				Text("H2").StyleAsH2(),
				Text("H3").StyleAsH3(),
				Text("H4").StyleAsH4(),
				Text("H5").StyleAsH5(),
				Text("H6").StyleAsH6(),
				Text("Subtitle 1").StyleAsSubtitle1(),
				Text("Subtitle 2").StyleAsSubtitle2(),
				Text("Body 1").StyleAsBody1(),
				Text("Body 2").StyleAsBody2(),
				Text("Caption").StyleAsBody2(),
				Text("OVERLINE").StyleAsOverline()
			)
		);
	}
}
