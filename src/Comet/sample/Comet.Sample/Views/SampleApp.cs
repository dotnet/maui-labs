using System;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class SampleApp : CometApp
	{
		public SampleApp()
		{
			//Body = () => new MainPage();
			Body = () => VStack(20,
				Text("Hey!!"),
				//Text("Hey!!"),
				Text("TEST PADDING").Frame(height:30).Margin(top:100),
				Text("This top part is a Microsoft.Maui.VerticalStackLayout"),
				HStack(2,
					Button("A Button").Frame(width:100).Color(Colors.White),
					Button("Hello I'm a button")
						.Color(Colors.Green)
						.Background(Colors.Purple),
					Text("And these buttons are in a HorizontalStackLayout")
				),
				Text("Hey!!"),
				Text("Hey!!")
				//new SecondView()

			).Background(Colors.Beige).Margin(top:30);
		}
	}
}
