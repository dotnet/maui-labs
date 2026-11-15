using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace HandlerTests;

public class WindowLifecycleTrackerTests
{
	[Fact]
	public void FullLifetime_FiresEachEventOnceInOrder()
	{
		var (window, calls) = RecordingWindow.Create();
		var tracker = new WindowLifecycleTracker(window);

		tracker.Create();
		tracker.Activate();
		tracker.Deactivate();
		tracker.Stop();
		tracker.Resume();
		tracker.Activate();
		tracker.Destroy();

		Assert.Equal(
			["Created", "Activated", "Deactivated", "Stopped", "Backgrounding", "Resumed", "Activated", "Deactivated", "Stopped", "Backgrounding", "Destroying"],
			calls);
	}

	[Fact]
	public void RepeatedPlatformEvents_FireEachTransitionOnce()
	{
		var (window, calls) = RecordingWindow.Create();
		var tracker = new WindowLifecycleTracker(window);

		tracker.Create();
		tracker.Create();
		tracker.Activate();
		tracker.Activate();
		tracker.Deactivate();
		tracker.Deactivate();
		tracker.Stop();
		tracker.Stop();
		tracker.Resume();
		tracker.Resume();
		tracker.Destroy();
		tracker.Destroy();

		Assert.Equal(
			["Created", "Activated", "Deactivated", "Stopped", "Backgrounding", "Resumed", "Stopped", "Backgrounding", "Destroying"],
			calls);
	}

	[Fact]
	public void ResumeOrDeactivateWithoutTheMatchingEvent_FiresNothing()
	{
		var (window, calls) = RecordingWindow.Create();
		var tracker = new WindowLifecycleTracker(window);

		tracker.Resume();
		tracker.Deactivate();

		Assert.Empty(calls);
	}

	[Fact]
	public void ActivatingAMinimizedWindow_ResumesItFirst()
	{
		var (window, calls) = RecordingWindow.Create();
		var tracker = new WindowLifecycleTracker(window);

		tracker.Activate();
		tracker.Deactivate();
		tracker.Stop();
		calls.Clear();

		tracker.Activate();

		Assert.Equal(["Resumed", "Activated"], calls);
	}

	[Fact]
	public void ClosingAnActiveWindow_DeactivatesAndStopsBeforeDestroying()
	{
		var (window, calls) = RecordingWindow.Create();
		var tracker = new WindowLifecycleTracker(window);

		tracker.Activate();
		calls.Clear();
		tracker.Destroy();

		Assert.Equal(["Deactivated", "Stopped", "Backgrounding", "Destroying"], calls);
	}

	[Fact]
	public void EventsAfterDestroy_AreIgnored()
	{
		var (window, calls) = RecordingWindow.Create();
		var tracker = new WindowLifecycleTracker(window);

		tracker.Destroy();
		calls.Clear();
		tracker.Create();
		tracker.Activate();
		tracker.Stop();

		Assert.Empty(calls);
	}

	[Fact]
	public void Backgrounding_ReceivesAPersistedState()
	{
		var (window, _) = RecordingWindow.Create();
		var tracker = new WindowLifecycleTracker(window);

		tracker.Stop();

		Assert.IsAssignableFrom<IPersistedState>(((RecordingWindow)(object)window).LastBackgroundingState);
	}

	/// <summary>A fake <see cref="IWindow"/> that records the lifecycle methods called on it.</summary>
	public class RecordingWindow : DispatchProxy
	{
		static readonly HashSet<string> LifecycleMethods =
			["Created", "Activated", "Deactivated", "Stopped", "Resumed", "Destroying", "Backgrounding"];

		public List<string> Calls { get; } = [];

		public object? LastBackgroundingState { get; private set; }

		public static (IWindow Window, List<string> Calls) Create()
		{
			var window = DispatchProxy.Create<IWindow, RecordingWindow>();
			return (window, ((RecordingWindow)(object)window).Calls);
		}

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			var name = targetMethod?.Name ?? "";
			if (LifecycleMethods.Contains(name))
				Calls.Add(name);
			if (name == "Backgrounding")
				LastBackgroundingState = args?[0];
			return null;
		}
	}
}
