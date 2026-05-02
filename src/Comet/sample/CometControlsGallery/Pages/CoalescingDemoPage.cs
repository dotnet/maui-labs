using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	/// <summary>
	/// Validates ReactiveScheduler coalescing behavior. When many signal writes happen
	/// synchronously (within a single event handler), only ONE UI rebuild should occur.
	/// A render counter tracks how many times Body actually executes.
	/// </summary>
	public class CoalescingDemoPage : View
	{
		readonly Reactive<int> counter = 0;
		readonly Reactive<string> status = "Ready";
		readonly Reactive<bool> timerRunning = false;

		int _bodyExecutionCount;

		[Body]
		View body()
		{
			_bodyExecutionCount++;

			return GalleryPageHelpers.Scaffold("Coalescing Demo",
				GalleryPageHelpers.Section("Synchronous Burst",
					GalleryPageHelpers.Caption("Writes 100 signal updates in a tight loop. " +
						"ReactiveScheduler should batch these into a single UI rebuild."),
					Button("Write 100x synchronously", () =>
					{
						var before = _bodyExecutionCount;
						for (int i = 0; i < 100; i++)
							counter.Value++;
						status.Value = $"Wrote 100x. Counter={counter.Value}. " +
							$"Body calls before={before}, check after flush.";
					}),
					Text(() => $"Counter: {counter.Value}")
						.FontSize(24)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.DodgerBlue),
					Text(() => $"Body executions: {_bodyExecutionCount}")
						.FontSize(16)
						.Color(Colors.MediumPurple),
					Text(() => status.Value)
						.FontSize(12)
						.Color(Colors.Grey)
				),
				GalleryPageHelpers.Section("Async Burst",
					GalleryPageHelpers.Caption("Fires 10 writes from a background thread. " +
						"Each write schedules a flush; scheduler coalesces them."),
					Button("Write 10x from background", () =>
					{
						status.Value = "Background writes started...";
						Task.Run(() =>
						{
							for (int i = 0; i < 10; i++)
								counter.Value++;

							Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
								status.Value = $"Background done. Counter={counter.Value}");
						});
					})
				),
				GalleryPageHelpers.Section("Timer Stress",
					GalleryPageHelpers.Caption("A timer writes to the counter every 100ms. " +
						"UI should update smoothly without freezing."),
					Button(() => timerRunning.Value ? "Stop Timer" : "Start Timer", () =>
					{
						timerRunning.Value = !timerRunning.Value;
						if (timerRunning.Value)
							StartTimer();
					}),
					Text(() => timerRunning.Value ? "Timer running..." : "Timer stopped")
						.FontSize(14)
						.Color(timerRunning.Value ? Colors.MediumSeaGreen : Colors.Grey)
				),
				GalleryPageHelpers.Section("Reset",
					Button("Reset all counters", () =>
					{
						counter.Value = 0;
						_bodyExecutionCount = 0;
						status.Value = "Reset complete";
					})
				),
				GalleryPageHelpers.Section("Validation Notes",
					GalleryPageHelpers.BodyText("100 synchronous writes → single body rebuild"),
					GalleryPageHelpers.BodyText("Background thread writes dispatch correctly"),
					GalleryPageHelpers.BodyText("Timer stress test — no UI freeze"),
					GalleryPageHelpers.BodyText("Body execution count tracks actual rebuilds")
				)
			);
		}

		async void StartTimer()
		{
			while (timerRunning.Value)
			{
				await Task.Delay(100);
				if (timerRunning.Value)
					counter.Value++;
			}
		}
	}
}
