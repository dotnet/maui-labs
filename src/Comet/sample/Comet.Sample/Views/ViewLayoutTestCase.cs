using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Comet.CometControls;

namespace Comet.Samples
{
	public  class ViewLayoutTestCase : Component
	{
				public override View Render() => ScrollView(
			VStack(

				VStack(
					Text(()=> "Recommended")
						.Color(Colors.Black)
						.FontFamily("Rockolf Bold")
						.FontSize(20)
						.FontWeight(FontWeight.Bold)
						.Margin(new Thickness(0, 6)),
					ScrollView(Orientation.Horizontal,
						new HStack {
							Enumerable.Range(0,10).Select(destination => ZStack(
							// Destination Background Image
							Image()
								.Background(Colors.SkyBlue).FillHorizontal().FillVertical(),
							VStack(LayoutAlignment.Start,
								VStack(
									Text(() => "$100")
										.Color(Colors.White)
										.FitHorizontal()
										.FontSize(14)
										.FontFamily("Rockolf Bold")
										.FontSize(14)
										.FontWeight(FontWeight.Bold)
								).FitHorizontal().Alignment( Alignment.Trailing)
									.Background(Color.FromArgb("#67AEE9"))
									.ClipShape(new RoundedRectangle(12))
									.Padding(6)
									.Margin(12),

								Spacer(),
								Text("Japan Street")
									.Color(Colors.White)
									.FontFamily("Rockolf Bold")
									.FontSize(18)
									.FontWeight(FontWeight.Bold)
									.Shadow(radius: 6),
								Text("Awesome Sauce")
									.Color(Colors.White)
									.FontFamily("Rockolf")
									.FontSize(14)
							)
							.Padding(new Thickness(16, 0, 0, 16))
						).ClipShape(new RoundedRectangle(36)).Frame(height: 250, width: 200))
						}
					)
				),


				Text("ZSTack Alignment"),
				ZStack(
					Text("TL").Background(Colors.Blue).Frame(75,75).Alignment(Alignment.TopLeading),
					Text("T").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Top),
					Text("TR").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.TopTrailing),
					Text("R").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Trailing),
					Text("L").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Leading),
					Text("BL").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.BottomLeading),
					Text("BR").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.BottomTrailing),
					Text("B").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Bottom)
				).Frame(400,400).Padding(12)
				.Background(Colors.White),

				Text("HStack, Only uses Vertial Alignment"),
				HStack(
					Text("T").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Top),
					Text("Center").Background(Colors.Blue).Frame(75,75).Alignment(Alignment.Center),
					Text("B").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Bottom)

				).Frame(400,400).Background(Colors.White).Padding(12),

				Text("VStack, Only uses Horizontal Alignment"),
				VStack(
					Text("L").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Leading),
					Text("C").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Top),
					Text("R").Background(Colors.Blue).Frame(75,75).Alignment( Alignment.Trailing)

				).Frame(400,400).Background(Colors.White).Padding(12),

				Text("VStack Without Spacers"),
				VStack(
					Text("L").Background(Colors.Blue),
					Text("C").Background(Colors.Blue),
					Text("R").Background(Colors.Blue)

				).Frame(400,400).Background(Colors.White).Padding(12),

				Text("VStack With Spacers"),
				VStack(
					Text("L").Background(Colors.Blue),
					Spacer(),
					Text("C").Background(Colors.Blue),
					Text("R").Background(Colors.Blue)

				).Frame(200,200).Background(Colors.White).Padding(12),

				Text("HStack Without Spacers"),
				HStack(
					Text("L").Background(Colors.Blue),
					Text("C").Background(Colors.Blue),
					Text("R").Background(Colors.Blue)

				).Background(Colors.White).Padding(12),

				Text("HStack With Spacers"),
				HStack(
					Text("L").Background(Colors.Blue),
					Spacer(),
					Text("C").Background(Colors.Blue),
					Text("R").Background(Colors.Blue)

				).Frame(200,200).Background(Colors.White).Padding(12)

			)
		);
	}
}
