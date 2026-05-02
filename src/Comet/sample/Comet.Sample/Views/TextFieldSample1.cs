using System;
using System.Collections.Generic;
using static Comet.CometControls;
using Comet.Reactive;

namespace Comet.Samples
{
	public class TextFieldSample1 : Component
	{
		readonly Signal<string> name1 = new("");

				public override View Render() => VStack(
			TextField(name1, "Name", ()=>{
				Console.WriteLine("Completed");
			}),
			
			HStack(
				Text("onCommit:"),
				Text(() => name1.Value),
				Spacer()
			)
		).FillHorizontal();
	}

}
