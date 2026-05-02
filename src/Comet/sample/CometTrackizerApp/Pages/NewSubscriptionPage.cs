using CometTrackizerApp.Components;
using CometTrackizerApp.Models;

namespace CometTrackizerApp.Pages;

public class NewSubscriptionState
{
	public SubscriptionType SelectedType { get; set; }
	public string Description { get; set; } = "";
}

public class NewSubscriptionPage : Component<NewSubscriptionState>
{
	static readonly SubscriptionType[] _subscriptionTypes = Enum.GetValues<SubscriptionType>();

	readonly Action _onClose;

	public NewSubscriptionPage(Action onClose)
	{
		_onClose = onClose;
	}

	public override View Render() => new ZStack
	{
		// Top section with subscription selector
		Border(
			VStack(16,
				TrackizerTheme.H7("Add new subscription")
					.Color(TrackizerTheme.White)
					.FontWeight(FontWeight.Bold)
					.HorizontalTextAlignment(TextAlignment.Center)
					.Margin(new Thickness(0, 96, 0, 0)),

				// Simplified subscription type selector (horizontal row of icons)
				HStack(16,
					_subscriptionTypes.Select(type =>
						Image($"{type.ToString().ToLower()}.png")
							.Frame(
								width: State.SelectedType == type ? 80 : 50,
								height: State.SelectedType == type ? 80 : 50)
							.Alignment(Alignment.Center)
							.OnTap(_ => SetState(s => s.SelectedType = type))
					).ToArray()
				).Alignment(Alignment.Center),

				TrackizerTheme.H2(State.SelectedType.GetDisplayName() ?? "")
					.Color(TrackizerTheme.White)
					.HorizontalTextAlignment(TextAlignment.Center)
			)
		)
		.Background(TrackizerTheme.Grey70)
		.ClipShape(new RoundedRectangle(24))
		.Frame(height: 400)
		.Alignment(Alignment.Top),

		// Back button
		Image("back.png")
			.Frame(width: 24, height: 24)
			.Alignment(Alignment.TopLeading)
			.Margin(new Thickness(24, 32, 0, 0))
			.OnTap(_ => _onClose()),

		// Title
		TrackizerTheme.H3("New")
			.Color(TrackizerTheme.Grey30)
			.HorizontalTextAlignment(TextAlignment.Center)
			.Alignment(Alignment.Top)
			.Margin(new Thickness(0, 32, 0, 0)),

		// Bottom form section
		VStack(24,
			new RoundedEntryView("Description", State.Description, false,
				text => SetState(s => s.Description = text)),

			new PriceEditor(),

			TrackizerTheme.PrimaryButton("Add this platform", _onClose)
		)
		.Alignment(Alignment.Bottom)
		.Margin(new Thickness(24, 0, 24, 32))
	}
	.Background(TrackizerTheme.Grey80);
}
