using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class PanGestureTrackerTests
{
	[Fact]
	public void Drag_ReportsStartedRunningAndCompletedWithOneGestureId()
	{
		var (recognizer, view, events) = CreatePan();
		var tracker = new PanGestureTracker(recognizer);

		tracker.Begin();
		tracker.Update(5, 2);
		tracker.Update(12, -4);
		tracker.End();

		Assert.Equal(
			[GestureStatus.Started, GestureStatus.Running, GestureStatus.Running, GestureStatus.Completed],
			events.Select(e => e.Args.StatusType));
		Assert.Single(events.Select(e => e.Args.GestureId).Distinct());
		Assert.All(events, e => Assert.Same(view, e.Sender));
		Assert.Equal((5d, 2d), (events[1].Args.TotalX, events[1].Args.TotalY));
		Assert.Equal((12d, -4d), (events[2].Args.TotalX, events[2].Args.TotalY));
	}

	[Fact]
	public void PressAndReleaseWithoutMovement_ReportsNothing()
	{
		var (recognizer, _, events) = CreatePan();
		var tracker = new PanGestureTracker(recognizer);

		tracker.Begin();
		tracker.End();

		Assert.Empty(events);
		Assert.False(tracker.IsPanning);
	}

	[Fact]
	public void Cancel_ReportsCanceledAndIgnoresTheFollowingEnd()
	{
		var (recognizer, _, events) = CreatePan();
		var tracker = new PanGestureTracker(recognizer);

		tracker.Begin();
		tracker.Update(3, 3);
		tracker.Cancel();
		// GTK emits drag-end after cancel.
		tracker.End();

		Assert.Equal(
			[GestureStatus.Started, GestureStatus.Running, GestureStatus.Canceled],
			events.Select(e => e.Args.StatusType));
	}

	[Fact]
	public void UpdateWithoutBegin_ReportsNothing()
	{
		var (recognizer, _, events) = CreatePan();
		var tracker = new PanGestureTracker(recognizer);

		tracker.Update(10, 10);

		Assert.Empty(events);
	}

	[Fact]
	public void EachPan_GetsANewGestureId()
	{
		var (recognizer, _, events) = CreatePan();
		var tracker = new PanGestureTracker(recognizer);

		tracker.Begin();
		tracker.Update(1, 1);
		tracker.End();
		tracker.Begin();
		tracker.Update(1, 1);
		tracker.End();

		var ids = events.Select(e => e.Args.GestureId).Distinct().ToList();
		Assert.Equal(2, ids.Count);
	}

	[Fact]
	public void DetachedRecognizer_ReportsNothing()
	{
		var recognizer = new PanGestureRecognizer();
		var events = new List<PanUpdatedEventArgs>();
		recognizer.PanUpdated += (_, e) => events.Add(e);
		var tracker = new PanGestureTracker(recognizer);

		tracker.Begin();
		tracker.Update(4, 4);
		tracker.End();

		Assert.Empty(events);
	}

	[Theory]
	[InlineData(1u, ButtonsMask.Primary)]
	[InlineData(3u, ButtonsMask.Secondary)]
	public void GdkButton_MapsToButtonsMask(uint button, ButtonsMask expected)
	{
		Assert.Equal(expected, GtkGestureExtensions.ToButtonsMask(button));
	}

	static (PanGestureRecognizer Recognizer, View View, List<(object? Sender, PanUpdatedEventArgs Args)> Events) CreatePan()
	{
		var recognizer = new PanGestureRecognizer();
		var view = new BoxView();
		view.GestureRecognizers.Add(recognizer);
		var events = new List<(object? Sender, PanUpdatedEventArgs Args)>();
		recognizer.PanUpdated += (sender, e) => events.Add((sender, e));
		return (recognizer, view, events);
	}
}
