using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace CometNodeApp1;

/// <summary>
/// The shared Comet view tree. It is rendered NATIVELY by Comet's node backend —
/// Jetpack Compose on Android, SwiftUI on iOS — with no MAUI handlers in the render
/// path. The same <see cref="Build"/> tree drives both platforms.
/// </summary>
public static class App
{
	static readonly Signal<int> Count = new(0);

	/// <param name="topInset">Dp of top padding to clear the platform's status bar / notch.
	/// The node backend does not apply safe-area insets automatically yet, so each platform
	/// head passes its own (Android status bar ~24, iOS notch / Dynamic Island ~50).</param>
	public static View Build(double topInset = 0) => new VStack(spacing: 16f)
	{
		new Text("Hello from Comet 👋").FontSize(28).Color(Colors.White),
		new Text(() => $"Count: {Count.Value}").FontSize(20).Color(Colors.White),
		new Button("Increment", () => Count.Value++),
	}
	.Background(Color.FromArgb("#6750A4"))
	.Padding(new Thickness(24, topInset + 24, 24, 24));

	/// <summary>A backend needs an <see cref="System.IServiceProvider"/> (its DI container).
	/// This starter app has no registered services.</summary>
	public sealed class Services : System.IServiceProvider
	{
		public object? GetService(System.Type serviceType) => null;
	}
}
