using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

/*
 // Flutter only support if render
class MyWidget extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Column(
      children: <Widget>[
        if (someCondition == true)
          Text('The condition is true!'),
      ],
    );
  }
}

// Flutter render list
class MyWidget extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Column(
      children: <Widget>[
        if (someCondition == true) ...[
          Text('Widget A'),
          Text('Widget B'),
          Text('Widget C'),
        ],
      ],
    );
  }
}

 */

namespace Comet.Samples.Comparisons
{
	public class Section5 : Component
	{
		bool showMore = true;
		int[] nums = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
		string fillColor = "Red";
				public override View Render() => new VStack {
				Text("Hello Comet!"),
				() => {
					if(showMore)
						return Text("If condition is working");
					else
						return Text("Else condition is working");
				},
				nums.Select(i => Text($"Show rows {i}")),
				showMore?Text("Ternary is working"):null,
				() => {
					switch(fillColor)
					{
						case "Red":
							return new ShapeView(new Rectangle().Fill(Colors.Red)).Frame(100, 60);
						default:
							return null;
					}
				}
			};

	}
}

