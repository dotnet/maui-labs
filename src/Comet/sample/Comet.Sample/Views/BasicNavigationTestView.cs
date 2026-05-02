using System;
using System.Collections.Generic;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class BasicNavigationTestView : Component
	{
				public override View Render() => NavigationView(
			VStack(
				Button("Navigate!",()=>{
					Navigation.Navigate(new BasicTestView());
				})
			)
		);
	}



}
