using System;
using System.Linq;
using CometView = Comet.View;

namespace Comet.Reactive;

public static class ReactiveDiagnostics
{
	public static bool IsEnabled { get; set; }

	static event Action<int>? FlushDepthWarning;
	static event Action<ViewRebuildEvent>? ViewRebuilt;
	static event Action<SignalChangeEvent>? SignalChanged;

	public static IDisposable OnViewRebuilt(Action<ViewRebuildEvent> handler)
	{
		ViewRebuilt += handler;
		return new DiagnosticSubscription(() => ViewRebuilt -= handler);
	}

	public static IDisposable OnSignalChanged(Action<SignalChangeEvent> handler)
	{
		SignalChanged += handler;
		return new DiagnosticSubscription(() => SignalChanged -= handler);
	}

	public static IDisposable OnFlushDepthWarning(Action<int> handler)
	{
		FlushDepthWarning += handler;
		return new DiagnosticSubscription(() => FlushDepthWarning -= handler);
	}

	internal static void NotifyFlushDepthWarning(int depth)
	{
		if (!IsEnabled)
			return;
		FlushDepthWarning?.Invoke(depth);
	}

	internal static void NotifyViewRebuilt(CometView view, IReactiveSource? trigger)
	{
		if (!IsEnabled)
			return;

		ViewRebuilt?.Invoke(new ViewRebuildEvent(
			view.GetType().Name,
			trigger?.GetType().Name,
			DateTime.UtcNow));
	}

	internal static void NotifySignalChanged(IReactiveSource signal, string? name)
	{
		if (!IsEnabled)
			return;

		SignalChanged?.Invoke(new SignalChangeEvent(
			signal.GetType().GenericTypeArguments.FirstOrDefault()?.Name ?? "?",
			name,
			signal.Version,
			DateTime.UtcNow));
	}

	sealed class DiagnosticSubscription : IDisposable
	{
		Action? _unsubscribe;

		public DiagnosticSubscription(Action unsubscribe)
		{
			_unsubscribe = unsubscribe;
		}

		public void Dispose()
		{
			_unsubscribe?.Invoke();
			_unsubscribe = null;
		}
	}
}

public readonly record struct ViewRebuildEvent(
	string ViewType,
	string? TriggerType,
	DateTime Timestamp);

public readonly record struct SignalChangeEvent(
	string ValueType,
	string? Name,
	uint Version,
	DateTime Timestamp);
