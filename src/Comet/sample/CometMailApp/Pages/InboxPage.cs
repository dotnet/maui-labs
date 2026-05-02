using CometMailApp.Models;

namespace CometMailApp.Pages;

public class InboxPage : View
{
	readonly SignalList<EmailMessage> _messages = MailStore.Inbox;

	[Body]
	View body()
	{
		return VStack(
			// Toolbar
			HStack(
				Text(() => $"Inbox ({_messages.Count(m => !m.IsRead)} unread)")
					.FontSize(22)
					.FontWeight(FontWeight.Bold)
					.Color(Colors.Black)
					.FillHorizontal(),
				Button("Compose", () =>
					this.Navigate(new ComposePage()))
					.Background(Colors.Transparent)
					.FontSize(20)
					.Frame(width: 44, height: 44)
			)
			.Padding(new Thickness(16, 12)),

			// Message list
			ScrollView(Orientation.Vertical,
				VStack(MessageRows())
			)
		)
		.Title("Inbox");
	}

	View[] MessageRows()
	{
		var views = new View[_messages.Count];
		for (var i = 0; i < _messages.Count; i++)
		{
			views[i] = MessageRow(_messages[i]);
		}
		return views;
	}

	View MessageRow(EmailMessage msg)
	{
		return VStack(
			HStack(8,
				VStack(
					Text(msg.Sender)
						.FontSize(16)
						.FontWeight(msg.IsRead ? FontWeight.Regular : FontWeight.Bold)
						.Color(Colors.Black),
					Text(FormatDate(msg.ReceivedAt))
						.FontSize(12)
						.Color(Colors.Gray)
				)
				.FillHorizontal(),
				msg.IsStarred
					? Text("*").FontSize(16).Frame(width: 28)
					: Text("").Frame(width: 28)
			),
			Text(msg.Subject)
				.FontSize(14)
				.FontWeight(msg.IsRead ? FontWeight.Regular : FontWeight.Bold)
				.Color(Colors.DarkSlateGray),
			Text(msg.Preview)
				.FontSize(13)
				.Color(Colors.Gray)
				.LineBreakMode(LineBreakMode.TailTruncation)
		)
		.Padding(new Thickness(16, 10))
		.Background(msg.IsRead ? Colors.White : Color.FromArgb("#F0F4FF"))
		.OnTap(v =>
		{
			MailStore.MarkAsRead(msg.Id);
			this.Navigate(new DetailPage(msg));
		});
	}

	static string FormatDate(DateTime dt)
	{
		var diff = DateTime.Now - dt;
		if (diff.TotalMinutes < 60)
			return $"{(int)diff.TotalMinutes}m ago";
		if (diff.TotalHours < 24)
			return $"{(int)diff.TotalHours}h ago";
		return dt.ToString("MMM d");
	}
}
