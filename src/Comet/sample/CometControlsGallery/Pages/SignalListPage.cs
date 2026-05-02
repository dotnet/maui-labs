using System;
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	/// <summary>
	/// Validates SignalList&lt;T&gt; — reactive collection with add, remove, clear.
	/// The list is rendered via ScrollView/VStack pattern since SignalList tracks
	/// granular changes (Insert/Remove/Replace/Reset).
	/// </summary>
	public class SignalListPage : View
	{
		readonly SignalList<string> items = new();
		readonly Reactive<string> newItemText = "";
		readonly Reactive<int> addCount = 0;

		[Body]
		View body()
		{
			var itemViews = new View[items.Count];
			for (int i = 0; i < items.Count; i++)
			{
				var index = i;
				var text = items[index];
				itemViews[i] = HStack(8,
					Text($"• {text}")
						.FontSize(14),
					new Spacer(),
					Button("X", () =>
					{
						if (index < items.Count)
							items.RemoveAt(index);
					})
					.Frame(width: 32, height: 32)
					.Color(Colors.Crimson)
				)
				.Padding(new Thickness(8, 4));
			}

			return GalleryPageHelpers.Scaffold("Signal List",
				GalleryPageHelpers.Section("Add Items",
					HStack(8,
						TextField(() => newItemText.Value, () => "Item text...")
							.OnTextChanged(v => newItemText.Value = v ?? ""),
						Button("Add", () =>
						{
							var text = string.IsNullOrWhiteSpace(newItemText.Value)
								? $"Item {++addCount.Value}"
								: newItemText.Value;
							items.Add(text);
							newItemText.Value = "";
						})
						.Frame(width: 60, height: 36)
					),
					HStack(12,
						Button("Add 5", () =>
						{
							for (int i = 0; i < 5; i++)
								items.Add($"Batch {++addCount.Value}");
						}),
						Button("Clear All", () =>
						{
							items.Clear();
						})
						.Color(Colors.Crimson)
					)
				),
				GalleryPageHelpers.Section($"Items ({items.Count})",
					items.Count == 0
						? (View)Text("No items — add some above!")
							.FontSize(14)
							.Color(Colors.Grey)
						: VStack(4, itemViews)
				),
				GalleryPageHelpers.Section("Validation Notes",
					GalleryPageHelpers.BodyText("SignalList<string> Add/RemoveAt/Clear"),
					GalleryPageHelpers.BodyText("Body rebuilds on every list mutation"),
					GalleryPageHelpers.BodyText("Batch add (5 items in tight loop)"),
					GalleryPageHelpers.BodyText($"Current count: {items.Count}")
				)
			);
		}
	}
}
