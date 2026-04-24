using System;
using System.Collections.Generic;
using System.Linq;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	public class ToolbarState
	{
		public int ItemCount { get; set; }
		public string StatusText { get; set; } = "Use buttons below to add, remove, and configure toolbar items.";
		public Color StatusColor { get; set; } = Colors.Grey;
		public string CountText { get; set; } = "Current toolbar items: 0";
		public List<string> ItemDescriptions { get; set; } = new();
	}

	public class ToolbarPage : Component<ToolbarState>
	{
		public override View Render() => GalleryPageHelpers.Scaffold("Toolbar",
			// Title
			Text("Toolbar Demo")
				.FontSize(28)
				.FontWeight(FontWeight.Bold),

			Text("Test adding, removing, and configuring toolbar items at runtime. "
				+ "Items can be placed in the content area (default) or the sidebar titlebar area "
				+ "when using a native sidebar layout.")
				.FontSize(14)
				.Color(Colors.Grey),

			// Info card
			Border(
				VStack(6,
					Text("How It Works")
						.FontSize(16)
						.FontWeight(FontWeight.Bold),
					Text("  Page.ToolbarItems maps to NSToolbarItems in the window toolbar")
						.FontSize(13)
						.Color(Colors.Grey),
					Text("  Items can show text, SF Symbol icons, or both")
						.FontSize(13)
						.Color(Colors.Grey),
					Text("  Primary items display in the toolbar; secondary items go to overflow")
						.FontSize(13)
						.Color(Colors.Grey),
					Text("  Enabled/disabled state is reflected in real-time")
						.FontSize(13)
						.Color(Colors.Grey)
				)
				.Padding(new Thickness(16))
			)
			.StrokeColor(Colors.CornflowerBlue)
			.StrokeThickness(1)
			.CornerRadius(8),

			// Status section
			GalleryPageHelpers.Section("Status",
				Text(() => State.CountText)
					.FontSize(14),
				Text(() => State.StatusText)
					.FontSize(14)
					.Color(() => State.StatusColor)
			),

			// Add Items section
			GalleryPageHelpers.Section("Add Items",
				Button("Add Text Item", AddTextItem)
					.Background(Colors.CornflowerBlue)
					.Color(Colors.White),
				Button("Add Disabled Item", AddDisabledItem)
					.Background(new Color(123, 104, 238))
					.Color(Colors.White)
			),

			// Add with Icons section
			GalleryPageHelpers.Section("Add with SF Symbol Icons",
				HStack(8,
					Button("plus.circle", () => AddIconItem("plus.circle"))
						.Background(Colors.Green)
						.Color(Colors.White)
						.FontSize(11),
					Button("trash", () => AddIconItem("trash"))
						.Background(Colors.Red)
						.Color(Colors.White)
						.FontSize(11),
					Button("square.and.pencil", () => AddIconItem("square.and.pencil"))
						.Background(Colors.Orange)
						.Color(Colors.White)
						.FontSize(11)
				),
				HStack(8,
					Button("magnifyingglass", () => AddIconItem("magnifyingglass"))
						.Background(Colors.Teal)
						.Color(Colors.White)
						.FontSize(11),
					Button("paperplane", () => AddIconItem("paperplane"))
						.Background(Colors.CornflowerBlue)
						.Color(Colors.White)
						.FontSize(11),
					Button("star.fill", () => AddIconItem("star.fill"))
						.Background(Colors.HotPink)
						.Color(Colors.White)
						.FontSize(11)
				)
			),

			// Manage Items section
			GalleryPageHelpers.Section("Manage Items",
				Button("Remove Last Item", RemoveLastItem)
					.Background(Colors.Orange)
					.Color(Colors.White),
				Button("Clear All Items", ClearAllItems)
					.Background(Colors.Red)
					.Color(Colors.White)
			),

			// Current Items section
			GalleryPageHelpers.Section("Current Items",
				VStack(4, BuildItemList())
			)
		);

		View[] BuildItemList()
		{
			if (State.ItemDescriptions.Count == 0)
			{
				return new View[]
				{
					Text("(no toolbar items)")
						.FontSize(13)
						.Color(Colors.Grey)
				};
			}

			return State.ItemDescriptions.Select(desc =>
				(View)Text(desc)
					.FontSize(13)
					.Color(new Color(60, 60, 67))
			).ToArray();
		}

		Microsoft.Maui.Controls.ContentPage GetPage()
		{
			var window = Microsoft.Maui.Controls.Application.Current?.Windows?.Count > 0
				? Microsoft.Maui.Controls.Application.Current.Windows[0]
				: null;
			return window?.Page as Microsoft.Maui.Controls.ContentPage;
		}

		void RefreshDisplay()
		{
			var page = GetPage();
			var count = page?.ToolbarItems?.Count ?? 0;
			SetState(s =>
			{
				s.CountText = $"Current toolbar items: {count}";
				s.ItemDescriptions = page?.ToolbarItems?
					.Select(t => $"  {t.Text} ({t.Order})")
					.ToList() ?? new List<string>();
			});
		}

		void AddTextItem()
		{
			var page = GetPage();
			if (page is null) return;

			SetState(s => s.ItemCount++);
			var captured = State.ItemCount;
			var item = new Microsoft.Maui.Controls.ToolbarItem
			{
				Text = $"Item {captured}",
			};
			item.Clicked += (_, _) =>
				SetState(s => { s.StatusText = $"Clicked: Item {captured}"; s.StatusColor = Colors.DodgerBlue; });

			page.ToolbarItems.Add(item);
			RefreshDisplay();
			SetState(s => { s.StatusText = $"Added text item: Item {captured}"; s.StatusColor = Colors.Green; });
		}

		void AddDisabledItem()
		{
			var page = GetPage();
			if (page is null) return;

			SetState(s => s.ItemCount++);
			var captured = State.ItemCount;
			var item = new Microsoft.Maui.Controls.ToolbarItem
			{
				Text = $"Disabled {captured}",
				Command = new Microsoft.Maui.Controls.Command(() => { }, () => false),
			};

			page.ToolbarItems.Add(item);
			RefreshDisplay();
			SetState(s => { s.StatusText = $"Added disabled item: Disabled {captured}"; s.StatusColor = Colors.Green; });
		}

		void AddIconItem(string symbol)
		{
			var page = GetPage();
			if (page is null) return;

			SetState(s => s.ItemCount++);
			var item = new Microsoft.Maui.Controls.ToolbarItem
			{
				Text = symbol,
				IconImageSource = symbol,
			};
			item.Clicked += (_, _) =>
				SetState(s => { s.StatusText = $"Clicked: {symbol}"; s.StatusColor = Colors.DodgerBlue; });

			page.ToolbarItems.Add(item);
			RefreshDisplay();
			SetState(s => { s.StatusText = $"Added icon item: {symbol}"; s.StatusColor = Colors.Green; });
		}

		void RemoveLastItem()
		{
			var page = GetPage();
			if (page is null || page.ToolbarItems.Count == 0) return;

			var last = page.ToolbarItems[page.ToolbarItems.Count - 1];
			page.ToolbarItems.Remove(last);
			RefreshDisplay();
			SetState(s => { s.StatusText = $"Removed: {last.Text}"; s.StatusColor = Colors.OrangeRed; });
		}

		void ClearAllItems()
		{
			var page = GetPage();
			if (page is null) return;

			page.ToolbarItems.Clear();
			SetState(s =>
			{
				s.ItemCount = 0;
				s.StatusText = "All toolbar items cleared";
				s.StatusColor = Colors.OrangeRed;
			});
			RefreshDisplay();
		}
	}
}
