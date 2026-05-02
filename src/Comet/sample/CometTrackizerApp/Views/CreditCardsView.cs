using CometTrackizerApp.Models;

namespace CometTrackizerApp.Views;

public class CreditCardsViewState
{
	public List<CreditCard> Cards { get; set; } =
	[
		new("John Doe", "1232", "08/25", "Virtual Card"),
		new("Aldo Rossi", "4534", "01/26", "Virtual Card"),
		new("Oliver James", "7867", "10/23", "Standard Card")
	];
}

public class CreditCardsView : Component<CreditCardsViewState>
{
	public override View Render() => new ZStack
	{
		// Card stack (simplified — static display, no pan gestures)
		CardStack(),

		// Subscriptions section
		VStack(16,
			TrackizerTheme.H3("Subscriptions")
				.Color(TrackizerTheme.White)
				.HorizontalTextAlignment(TextAlignment.Center),

			HStack(8,
				Enum.GetValues<SubscriptionType>().Select(type =>
					Image($"{type.ToString().ToLower()}.png")
						.Frame(width: 40, height: 40)
				).ToArray()
			).Alignment(Alignment.Center)
		)
		.Alignment(Alignment.Center)
		.Margin(new Thickness(0, 320, 0, 0)),

		// Add new card section at bottom
		Border(
			Border(
				HStack(10,
					TrackizerTheme.H2("Add new card")
						.Color(TrackizerTheme.Grey30),
					Image("add.png").Frame(width: 16, height: 16)
				).Alignment(Alignment.Center)
			)
			.RoundedBorder(radius: 16, color: TrackizerTheme.Grey60, strokeSize: 1)
			.Frame(height: 52)
			.Margin(new Thickness(24))
		)
		.Background(TrackizerTheme.Grey70)
		.ClipShape(new RoundedRectangle(24))
		.Frame(height: 185)
		.Alignment(Alignment.Bottom),
	};

	View CardStack()
	{
		var cards = State.Cards.Select((card, index) =>
			CardPlate(card, index)
		).ToArray();

		return new ZStack
		{
			cards
		}
		.Alignment(Alignment.Top)
		.Margin(new Thickness(0, 80, 0, 0));
	}

	View CardPlate(CreditCard card, int index) =>
		new ZStack
		{
			Image("card_plate.png")
				.Frame(width: 232),

			TrackizerTheme.BodyLarge(card.Type)
				.Color(TrackizerTheme.White)
				.HorizontalTextAlignment(TextAlignment.Center)
				.Alignment(Alignment.Top)
				.Margin(new Thickness(0, 82, 0, 0)),

			TrackizerTheme.H1(card.Holder)
				.Color(TrackizerTheme.Grey20)
				.HorizontalTextAlignment(TextAlignment.Center)
				.Alignment(Alignment.Center)
				.Margin(new Thickness(0, 20, 0, 0)),

			TrackizerTheme.BodyMedium(card.ExpiringDate)
				.Color(TrackizerTheme.White)
				.HorizontalTextAlignment(TextAlignment.Center)
				.Alignment(Alignment.Bottom)
				.Margin(new Thickness(0, 0, 0, 76)),
		}
		.Frame(width: 232)
		.Alignment(Alignment.Center)
		.Margin(new Thickness(0, index * 10, 0, 0));
}
