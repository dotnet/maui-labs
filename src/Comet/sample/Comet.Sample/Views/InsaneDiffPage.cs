using System;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class InsaneDiffPage : Component
	{
		readonly Reactive<bool> myBoolean = new Reactive<bool>();
		readonly Reactive<string> myText = new Reactive<string>();

				public override View Render()
		{
			var stack = VStack(
					Button(()=> myBoolean.Value ? myText.Value : $"State: {myBoolean.Value}",
						()=> myBoolean.Value = !myBoolean.Value)
				);
			for (var i = 0; i < 100; i++)
			{
				stack.Add(Text(i.ToString()));
			}
			return ScrollView( stack );
		}
	}
}
