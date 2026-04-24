using CometMailApp.Models;

namespace CometMailApp.Pages;

public class ComposeState
{
	public string To { get; set; } = "";
	public string Subject { get; set; } = "";
	public string Body { get; set; } = "";
}

public class ComposePage : Component<ComposeState>
{
	readonly EmailMessage? _replyTo;

	public ComposePage()
	{
	}

	public ComposePage(EmailMessage replyTo)
	{
		_replyTo = replyTo;
	}

	protected override void OnMounted()
	{
		if (_replyTo != null)
		{
			SetState(s =>
			{
				s.To = _replyTo.SenderEmail;
				s.Subject = $"Re: {_replyTo.Subject}";
				s.Body = $"\n\n--- Original message ---\n{_replyTo.Body}";
			});
		}
	}

	public override View Render()
	{
		return ScrollView(Orientation.Vertical,
			VStack(12,
				Text("New Message")
					.FontSize(22)
					.FontWeight(FontWeight.Bold)
					.Color(Colors.Black),

				// To field
				VStack(4,
					Text("To")
						.FontSize(13)
						.Color(Colors.Gray),
					TextField(State.To, "recipient@example.com")
						.OnTextChanged(t => SetState(s => s.To = t))
				),

				// Subject field
				VStack(4,
					Text("Subject")
						.FontSize(13)
						.Color(Colors.Gray),
					TextField(State.Subject, "Subject")
						.OnTextChanged(t => SetState(s => s.Subject = t))
				),

				// Separator
				new BoxView().Background(Colors.LightGray).Frame(height: 1),

				// Body field
				TextEditor(State.Body)
					.OnTextChanged(t => SetState(s => s.Body = t))
					.Frame(height: 300),

				// Send button
				Button("Send", () =>
				{
					if (!string.IsNullOrWhiteSpace(State.To) &&
						!string.IsNullOrWhiteSpace(State.Subject))
					{
						MailStore.SendMessage(State.To, State.Subject, State.Body);
						this.Dismiss();
					}
				})
				.Color(Colors.White)
				.Background(Colors.SlateBlue)
				.CornerRadius(8)
				.Padding(new Thickness(16, 10))
			)
			.Padding(new Thickness(20))
		)
		.Title("Compose");
	}
}
