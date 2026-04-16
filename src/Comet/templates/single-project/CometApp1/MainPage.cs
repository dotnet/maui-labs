using static Comet.CometControls;

namespace CometApp1;

public class MainPage : View
{
	readonly Reactive<int> count = 0;

	[Body]
	View body() => VStack(spacing: 16,
		Text(() => $"Count: {count.Value}")
			.FontSize(48)
			.FontWeight(FontWeight.Bold),
		Button("Increment", () => count.Value++),
		Button("Reset", () => count.Value = 0)
	).Alignment(Alignment.Center);
}
