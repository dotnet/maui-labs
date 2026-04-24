namespace CometAllTheLists.Pages;

public class Message
{
	public string From { get; set; } = "";
	public string Subject { get; set; } = "";
	public string Preview { get; set; } = "";
	public DateTime Date { get; set; }
	public bool IsUnread { get; set; }
}

public class InboxPageState
{
	public string SelectedMessage { get; set; } = "";
	public List<Message> Inbox { get; set; } = new List<Message>
	{
		new() { From = "Alice Johnson", Subject = "Project Update", Preview = "Here's the latest status on the Q2 project...", Date = DateTime.Now.AddHours(-2), IsUnread = true },
		new() { From = "Bob Smith", Subject = "Meeting Tomorrow", Preview = "Let me know if you're available at 2pm...", Date = DateTime.Now.AddHours(-5), IsUnread = true },
		new() { From = "Carol Davis", Subject = "Expense Report", Preview = "Please review the attached expense report...", Date = DateTime.Now.AddHours(-8), IsUnread = false },
		new() { From = "David Wilson", Subject = "Feedback on Design", Preview = "Great work on the mockups! A few suggestions...", Date = DateTime.Now.AddHours(-12), IsUnread = false },
		new() { From = "Eve Martinez", Subject = "Team Lunch Friday", Preview = "Are you interested in team lunch this Friday?...", Date = DateTime.Now.AddDays(-1), IsUnread = false },
		new() { From = "Frank Brown", Subject = "Code Review", Preview = "Please review PR #1234 when you have time...", Date = DateTime.Now.AddDays(-1), IsUnread = false },
	};
}

public class InboxPage : Component<InboxPageState>
{
	public override View Render()
	{
		return new Grid(
			rows: new object[] { "Auto", "Auto", "*" },
			columns: new object[] { "*" })
		{
			Text("Inbox")
				.FontSize(24)
				.FontWeight(FontWeight.Bold)
				.Padding(16)
				.Cell(row: 0, column: 0),

			Text(() => $"Selected: {State.SelectedMessage}")
				.FontSize(12)
				.Color(Colors.Gray)
				.Padding(8)
				.Cell(row: 1, column: 0),

			new CollectionView<Message>(() => State.Inbox)
			{
				ViewFor = msg => RenderMessageItem(msg),
				ItemSelected = selection =>
				{
					var tappedMessage = (Message)selection.item;
					SetState(s =>
					{
						s.SelectedMessage = tappedMessage.From;
						if (tappedMessage.IsUnread)
						{
							tappedMessage.IsUnread = false;
							s.Inbox = new List<Message>(s.Inbox);
						}
					});
				},
				Header = Text($"Messages ({State.Inbox.Count})")
					.FontSize(14)
					.Padding(12)
					.Background(new SolidPaint(Colors.LightGray)),
			}.Padding(8)
			 .Cell(row: 2, column: 0),
		};
	}

	View RenderMessageItem(Message msg)
	{
		return VStack(spacing: 2,
			HStack(spacing: 8,
				VStack(spacing: 4,
					HStack(spacing: 6,
						Text(msg.From)
							.FontSize(14)
							.FontWeight(FontWeight.Bold),
						msg.IsUnread ? new ShapeView(new Circle())
							.Frame(width: 8, height: 8)
							.Background(new SolidPaint(Colors.Blue))
							: (View)Text("")
					),
					Text(msg.Subject)
						.FontSize(12)
						.Color(Colors.Gray),
					Text(msg.Preview)
						.FontSize(11)
						.Color(Colors.DarkGray)
				),
				Spacer(),
				Text(FormatDate(msg.Date))
					.FontSize(10)
					.Color(Colors.Gray)
			)
		)
		.Padding(12)
		.Background(new SolidPaint(msg.IsUnread ? Color.FromArgb("#F0F8FF") : Colors.White))
		.OnTap(_ =>
		{
			SetState(s =>
			{
				s.SelectedMessage = msg.From;
				if (msg.IsUnread)
				{
					msg.IsUnread = false;
					s.Inbox = new List<Message>(s.Inbox);
				}
			});
		});
	}

	string FormatDate(DateTime date)
	{
		var diff = DateTime.Now - date;
		if (diff.TotalHours < 1) return "now";
		if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h";
		return $"{(int)diff.TotalDays}d";
	}
}
