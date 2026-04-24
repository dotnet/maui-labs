namespace CometAllTheLists.Pages;

public class Contact
{
	public string Name { get; set; } = "";
	public string Phone { get; set; } = "";
	public string Section { get; set; } = "";
}

public class AddressBookPageState
{
	public List<Contact> AllContacts { get; set; } = new List<Contact>
	{
		new() { Name = "Alice Anderson", Phone = "555-0101", Section = "A" },
		new() { Name = "Amy Austin", Phone = "555-0102", Section = "A" },

		new() { Name = "Bob Brown", Phone = "555-0201", Section = "B" },
		new() { Name = "Bill Baker", Phone = "555-0202", Section = "B" },
		new() { Name = "Brian Bennett", Phone = "555-0203", Section = "B" },

		new() { Name = "Carol Carter", Phone = "555-0301", Section = "C" },
		new() { Name = "Cathy Chen", Phone = "555-0302", Section = "C" },

		new() { Name = "David Davis", Phone = "555-0401", Section = "D" },
		new() { Name = "Danny Drake", Phone = "555-0402", Section = "D" },
		new() { Name = "Derek Duncan", Phone = "555-0403", Section = "D" },

		new() { Name = "Emma Evans", Phone = "555-0501", Section = "E" },

		new() { Name = "Frank Foster", Phone = "555-0601", Section = "F" },
		new() { Name = "Fred Fisher", Phone = "555-0602", Section = "F" },
	};
}

public class AddressBookPage : Component<AddressBookPageState>
{
	public override View Render()
	{
		var sections = State.AllContacts
			.GroupBy(c => c.Section)
			.OrderBy(g => g.Key)
			.ToList();

		return VStack(
			Text("Address Book")
				.FontSize(24)
				.FontWeight(FontWeight.Bold)
				.Padding(16),

			VStack(spacing: 0,
				sections.SelectMany(section =>
					new View[]
					{
						Text(section.Key)
							.FontSize(14)
							.FontWeight(FontWeight.Bold)
							.Padding(new Thickness(12, 8, 12, 4))
							.Background(new SolidPaint(Colors.LightGray)),
						VStack(spacing: 0,
							section.Select(contact => RenderContactItem(contact))
								.Cast<View>()
								.ToArray()
						),
					}
				).ToArray()
			)
		);
	}

	View RenderContactItem(Contact contact)
	{
		return VStack(spacing: 4,
			HStack(spacing: 12,
				new ShapeView(new Circle())
					.Frame(width: 40, height: 40)
					.Background(new SolidPaint(GetContactColor(contact.Name))),

				VStack(spacing: 2,
					Text(contact.Name)
						.FontSize(14)
						.FontWeight(FontWeight.Bold),
					Text(contact.Phone)
						.FontSize(12)
						.Color(Colors.Gray)
				),

				Spacer(),

				Text("→")
					.FontSize(16)
					.Color(Colors.LightGray)
			)
		)
		.Padding(12);
	}

	Color GetContactColor(string name)
	{
		var colors = new[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple, Colors.Pink };
		var hash = name.GetHashCode();
		return colors[((hash % colors.Length) + colors.Length) % colors.Length];
	}
}
