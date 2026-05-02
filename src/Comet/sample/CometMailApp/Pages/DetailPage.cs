using CometMailApp.Models;

namespace CometMailApp.Pages;

public class DetailPage : View
{
	readonly EmailMessage _message;

	public DetailPage(EmailMessage message)
	{
		_message = message;
	}

	[Body]
	View body()
	{
		return ScrollView(Orientation.Vertical,
			VStack(16,
				// Subject
				Text(_message.Subject)
					.FontSize(22)
					.FontWeight(FontWeight.Bold)
					.Color(Colors.Black),

				// Separator
				new BoxView().Background(Colors.LightGray).Frame(height: 1),

				// Sender info
				HStack(12,
					// Avatar circle
					Border(
						Text(_message.Sender[..1].ToUpper())
							.FontSize(18)
							.FontWeight(FontWeight.Bold)
							.Color(Colors.White)
							.HorizontalTextAlignment(TextAlignment.Center)
							.VerticalTextAlignment(TextAlignment.Center)
					)
					.Background(Colors.SlateBlue)
					.ClipShape(new RoundedRectangle(20))
					.Frame(width: 40, height: 40),

					VStack(2,
						Text(_message.Sender)
							.FontSize(16)
							.FontWeight(FontWeight.Bold)
							.Color(Colors.Black),
						Text($"<{_message.SenderEmail}>")
							.FontSize(13)
							.Color(Colors.Gray)
					)
					.FillHorizontal(),

					Text(FormatFullDate(_message.ReceivedAt))
						.FontSize(12)
						.Color(Colors.Gray)
				),

				// Separator
				new BoxView().Background(Colors.LightGray).Frame(height: 1),

				// Action buttons
				HStack(16,
					Button("Star", () => MailStore.ToggleStar(_message.Id))
						.FontSize(14)
						.Background(Colors.Transparent)
						.Color(Colors.SlateBlue),
					Button("Reply", () =>
						this.Navigate(new ComposePage(_message)))
						.FontSize(14)
						.Background(Colors.Transparent)
						.Color(Colors.SlateBlue),
					Button("Delete", () =>
					{
						MailStore.DeleteMessage(_message.Id);
						this.Dismiss();
					})
					.FontSize(14)
					.Background(Colors.Transparent)
					.Color(Colors.Crimson),
					Spacer()
				),

				// Separator
				new BoxView().Background(Colors.LightGray).Frame(height: 1),

				// Body text
				Text(_message.Body)
					.FontSize(15)
					.Color(Colors.DarkSlateGray)
					.LineBreakMode(LineBreakMode.WordWrap)
			)
			.Padding(new Thickness(20))
		)
		.Title(_message.Subject);
	}

	static string FormatFullDate(DateTime dt) =>
		dt.ToString("MMM d, yyyy h:mm tt");
}
