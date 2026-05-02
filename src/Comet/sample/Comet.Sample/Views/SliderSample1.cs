using System;
using System.Collections.Generic;
using static Comet.CometControls;
using Comet.Reactive;

/*

struct ContentView : View {
    @State var celsius: Double = 0

    var body: some View {
        VStack {
            Slider(value: $celsius, from: -100, through: 100, by: 0.1)
            Text("\(celsius) Celsius is \(celsius * 9 / 5 + 32) Fahrenheit")
        }
    }
}

*/
namespace Comet.Samples
{
	public class SliderSample1 : Component
	{
		readonly Signal<double> celsius = new(50);

				public override View Render() => VStack(
                //Slider(value: 12, from: -100, through: 100, by: 0.1f),
                //Slider(value: () => 12f, from: -100, through: 100, by: 0.1f),
                //Slider(value: new Binding<float>( getValue: () => 12f, setValue:null), from: -100, through: 100),
                Slider(value: celsius, minimum: -100, maximum: 100),
				Text(()=>$"{celsius.Value} Celsius"),
				Text(()=>$"{celsius.Value * 9 / 5 + 32} Fahrenheit")
			);

	}
}
