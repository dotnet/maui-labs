using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace test_go_app;

public class MainPage : View
{
    readonly Reactive<int> count = new(0);

    [Body]
    View body() => new VStack(spacing: 20)
    {
        new Text("Yo, this is the test-go-app!")
            .FontSize(28)
            .FontWeight(FontWeight.Bold)
            .Color(Colors.Orange)
            .HorizontalTextAlignment(TextAlignment.Center),

        new Text(() => $"Count: {count.Value}")
            .FontSize(22)
            .HorizontalTextAlignment(TextAlignment.Center),

        new Button("Tap me!", () => count.Value++)
            .Color(Colors.White)
            .Background(new SolidPaint(Colors.Orange))
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
