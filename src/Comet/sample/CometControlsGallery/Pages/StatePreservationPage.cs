using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	/// <summary>
	/// Validates state preservation across navigation. The parent page has a counter;
	/// pushing a child page and popping back should preserve the counter value.
	/// Also validates that disposed child views stop reacting to signals.
	/// </summary>
	public class StatePreservationPage : View
	{
		readonly Reactive<int> parentCounter = 0;
		readonly Reactive<string> parentNote = "Navigate to child, then come back.";
		readonly DateTimeOffset createdAt = DateTimeOffset.Now;

		[Body]
		View body() =>
			GalleryPageHelpers.Scaffold("State Preservation",
				GalleryPageHelpers.Section("Parent State",
					Text(() => $"Parent Counter: {parentCounter.Value}")
						.FontSize(32)
						.FontWeight(FontWeight.Bold)
						.HorizontalTextAlignment(TextAlignment.Center)
						.Color(Colors.DodgerBlue),
					HStack(16,
						Button("−", () => parentCounter.Value--)
							.Frame(width: 60, height: 44),
						Button("+", () => parentCounter.Value++)
							.Frame(width: 60, height: 44)
					),
					Text(() => parentNote.Value)
						.FontSize(14)
						.Color(Colors.Grey),
					GalleryPageHelpers.Caption($"Page created: {createdAt:hh:mm:ss tt}")
				),
				GalleryPageHelpers.Section("Navigation Test",
					GalleryPageHelpers.Caption("Push a child page, interact with it, then pop back. " +
						"The parent counter above should retain its value."),
					Button("Push Child Page", () =>
					{
						parentNote.Value = $"Navigated away with counter={parentCounter.Value}";
						Comet.NavigationView.Navigate(this,
							new StatePreservationChildPage(parentCounter.Value));
					})
				),
				GalleryPageHelpers.Section("Validation Notes",
					GalleryPageHelpers.BodyText("Parent state survives child push/pop"),
					GalleryPageHelpers.BodyText("createdAt timestamp proves same instance"),
					GalleryPageHelpers.BodyText("Child page has its own independent state"),
					GalleryPageHelpers.BodyText("Disposed child stops reacting to its signals")
				)
			);
	}

	/// <summary>
	/// Child page for the state preservation test. Has its own counter.
	/// When popped, its signals should stop triggering UI updates.
	/// </summary>
	public class StatePreservationChildPage : View
	{
		readonly Reactive<int> childCounter = 0;
		readonly int parentValueAtNav;
		readonly DateTimeOffset childCreatedAt = DateTimeOffset.Now;

		public StatePreservationChildPage(int parentValue)
		{
			parentValueAtNav = parentValue;
		}

		[Body]
		View body() =>
			GalleryPageHelpers.Scaffold("Child Page",
				GalleryPageHelpers.Section("Child State",
					Text(() => $"Child Counter: {childCounter.Value}")
						.FontSize(32)
						.FontWeight(FontWeight.Bold)
						.HorizontalTextAlignment(TextAlignment.Center)
						.Color(Colors.MediumSeaGreen),
					HStack(16,
						Button("−", () => childCounter.Value--)
							.Frame(width: 60, height: 44),
						Button("+", () => childCounter.Value++)
							.Frame(width: 60, height: 44)
					)
				),
				GalleryPageHelpers.Section("Context",
					GalleryPageHelpers.BodyText($"Parent counter was {parentValueAtNav} when navigated here"),
					GalleryPageHelpers.Caption($"Child created: {childCreatedAt:hh:mm:ss tt}"),
					Button("Pop back to parent", () =>
						Comet.NavigationView.Pop(this))
				)
			);
	}
}
