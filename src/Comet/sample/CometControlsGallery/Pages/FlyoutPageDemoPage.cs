using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class FlyoutPageState
	{
		public string SelectedTitle { get; set; } = "Welcome";
		public Color SelectedColor { get; set; } = Colors.DodgerBlue;
		public int MessageCount { get; set; } = 5;
	}

	public class FlyoutPageDemoPage : Component<FlyoutPageState>
	{
		static readonly (string Title, Color Color)[] MenuItems = new[]
		{
			("Inbox", Colors.DodgerBlue),
			("Starred", Colors.Gold),
			("Sent", Colors.MediumSeaGreen),
			("Drafts", Colors.Orange),
			("Trash", Colors.Red),
			("Archive", Colors.SlateGrey),
		};

		public override View Render() => GalleryPageHelpers.Scaffold("FlyoutPage Demo",
			// Title
			Text("FlyoutPage Demo")
				.FontSize(24)
				.FontWeight(FontWeight.Bold),

			Text(DeviceInfo.Platform == DevicePlatform.iOS
				? "Simulates a FlyoutPage with sidebar + detail. The real FlyoutPageHandler uses UISplitViewController."
				: "Simulates a FlyoutPage with sidebar + detail. The real FlyoutPageHandler uses NSSplitView.")
				.FontSize(13)
				.Color(Colors.Grey),

			// Sidebar + Detail layout (adaptive for phone)
			DeviceInfo.Idiom == DeviceIdiom.Phone
				? (View)VStack(12,
					// On phone, show menu as horizontal scroll strip
					ScrollView(Orientation.Horizontal,
						HStack(4, BuildMenuItems())
					).Frame(height: 44),
					Text(() => State.SelectedTitle)
						.FontSize(20)
						.FontWeight(FontWeight.Bold)
						.Color(() => State.SelectedColor),
					Text(() => $"{State.MessageCount} messages")
						.FontSize(14)
						.Color(Colors.Grey),
					BuildMessageList()
				)
				: Grid(
					new object[] { 200, "*" },
					null,
					ScrollView(
						VStack((float?)0, BuildMenuItems())
					)
					.Background(Color.FromArgb("#F5F5F5"))
					.Frame(height: 400)
					.Cell(row: 0, column: 0),
					ScrollView(
						VStack(12,
							Text(() => State.SelectedTitle)
								.FontSize(20)
								.FontWeight(FontWeight.Bold)
								.Color(() => State.SelectedColor),
							Text(() => $"{State.MessageCount} messages")
								.FontSize(14)
								.Color(Colors.Grey),
							BuildMessageList()
						)
						.Padding(new Thickness(24))
					)
					.Frame(height: 400)
					.Cell(row: 0, column: 1)
				)
		);

		View[] BuildMenuItems()
		{
			var items = new View[MenuItems.Length + 2];
			items[0] = Text("Mail")
				.FontSize(18)
				.FontWeight(FontWeight.Bold)
				.Padding(new Thickness(16, 16, 16, 8));
			items[1] = GalleryPageHelpers.Separator();

			for (int i = 0; i < MenuItems.Length; i++)
			{
				var (title, color) = MenuItems[i];
				var capturedTitle = title;
				var capturedColor = color;
				items[i + 2] = Button($"  {title}", () =>
				{
					var rng = new Random();
					SetState(s =>
					{
						s.SelectedTitle = capturedTitle;
						s.SelectedColor = capturedColor;
						s.MessageCount = rng.Next(3, 12);
					});
				})
				.Color(Colors.Black)
				.Background(Colors.Transparent)
				.FontSize(14);
			}
			return items;
		}

		View BuildMessageList()
		{
			var messages = new View[Math.Min(State.MessageCount, 5)];
			for (int i = 0; i < messages.Length; i++)
			{
				var senderColor = i % 3 == 0 ? Colors.Coral : i % 2 == 0 ? Colors.CornflowerBlue : Colors.MediumSeaGreen;
				var letter = ((char)('A' + i)).ToString();
				messages[i] = Border(
					VStack(4,
						HStack(8,
							Border(
								Text(letter)
									.Color(Colors.White)
									.FontSize(11)
							)
							.Background(senderColor)
							.CornerRadius(12)
							.Frame(width: 24, height: 24),
							Text($"Sender {i + 1}")
								.FontSize(14)
								.FontWeight(FontWeight.Bold)
						),
						Text($"Message preview for {State.SelectedTitle.ToLower()} item #{i + 1}...")
							.FontSize(12)
							.Color(Colors.DimGrey)
					)
					.Padding(new Thickness(12, 8))
				)
				.StrokeColor(Color.FromArgb("#e0e0e0"))
				.StrokeThickness(1)
				.CornerRadius(8);
			}
			return VStack(8, messages);
		}
	}
}
