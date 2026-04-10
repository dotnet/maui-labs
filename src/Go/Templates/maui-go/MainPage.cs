// My first MAUI Go app!
// Edit this file and save — the companion app updates instantly.

using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace MauiGoApp;

public class MainPage : View
{
	readonly Reactive<int> count = new(0);

	[Body]
	View body() => new VStack(spacing: 20)
	{
		new Text("Welcome to MAUI Go!")
			.FontSize(28)
			.FontWeight(FontWeight.Bold)
			.Color(Colors.Purple)
			.HorizontalTextAlignment(TextAlignment.Center),

		new Text(() => $"Count: {count.Value}")
			.FontSize(22)
			.HorizontalTextAlignment(TextAlignment.Center),

		new Button("Tap me!", () => count.Value++)
			.Color(Colors.White)
			.Background(new SolidPaint(Colors.Purple))
			.CornerRadius(12)
			.Frame(height: 50),

		new Text("Edit MainPage.cs and save — the UI updates live!")
			.FontSize(14)
			.Color(Colors.Gray)
			.HorizontalTextAlignment(TextAlignment.Center),
	}
	.Padding(new Thickness(32))
	.Alignment(Alignment.Center);
}
