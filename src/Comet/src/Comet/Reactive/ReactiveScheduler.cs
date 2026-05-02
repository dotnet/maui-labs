using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Maui.Controls;
using CometView = Comet.View;
using ThreadHelper = Comet.ThreadHelper;

namespace Comet.Reactive;

public static class ReactiveScheduler
{
	static volatile bool _flushScheduled;
	static volatile bool _flushing;
	static readonly HashSet<Effect> _dirtyEffects = new();
	static readonly HashSet<CometView> _dirtyViews = new();
	static readonly object _lock = new();

	internal const int MaxFlushDepth = 100;

	/// <summary>
	/// When true, <see cref="MarkViewDirty"/> and <see cref="ScheduleEffect"/> are no-ops.
	/// Used during <see cref="DatabindingExtensions.DiffUpdate"/> → UpdateFromOldView to
	/// prevent environment property transfers (Gestures, ViewHandler, etc.) from re-dirtying
	/// views that are already being rebuilt.
	/// </summary>
	[ThreadStatic]
	static bool _suppressNotifications;

	internal static bool SuppressNotifications
	{
		get => _suppressNotifications;
		set => _suppressNotifications = value;
	}

	public static void EnsureFlushScheduled()
	{
		// If a flush is already in progress, skip scheduling — the current
		// Flush loop's hasMore check will pick up newly-dirtied views/effects.
		// This prevents StackOverflow when Reload → SetEnvironment → NotifyChanged
		// → MarkViewDirty → EnsureFlushScheduled would otherwise re-enter FlushEntry.
		if (_flushScheduled || _flushing)
			return;

		lock (_lock)
		{
			if (_flushScheduled)
				return;
			_flushScheduled = true;
		}

		var scheduled = false;
		if (Application.Current?.Dispatcher is { } dispatcher)
		{
			try
			{
				if (dispatcher.IsDispatchRequired)
					scheduled = dispatcher.Dispatch(FlushEntry);
				else
				{
					FlushEntry();
					scheduled = true;
				}
			}
			catch
			{
				scheduled = false;
			}
		}

		if (!scheduled)
		{
			try
			{
				ThreadHelper.RunOnMainThread(FlushEntry);
				scheduled = true;
			}
			catch
			{
				scheduled = false;
			}
		}

		if (!scheduled)
		{
			lock (_lock)
			{
				_flushScheduled = false;
			}
		}
	}

	internal static void ScheduleEffect(Effect effect)
	{
		if (_suppressNotifications)
			return;
		lock (_lock)
		{
			_dirtyEffects.Add(effect);
		}
		EnsureFlushScheduled();
	}

	internal static void MarkViewDirty(CometView view)
	{
		if (_suppressNotifications)
			return;
		lock (_lock)
		{
			_dirtyViews.Add(view);
		}
		EnsureFlushScheduled();
	}

	static void FlushEntry()
	{
		lock (_lock)
		{
			_flushScheduled = false;
		}

		_flushing = true;
		try
		{
			Flush(depth: 0);
		}
		finally
		{
			_flushing = false;
		}
	}

	static void Flush(int depth)
	{
		if (depth >= MaxFlushDepth)
		{
			ReactiveDiagnostics.NotifyFlushDepthWarning(depth);

			Debug.WriteLine(
				$"[Comet.Reactive] ReactiveScheduler exceeded {MaxFlushDepth} flush iterations. " +
				"This indicates a cycle in the reactive graph (effects writing signals that " +
				"trigger other effects in a loop). Breaking the cycle. UI may show stale data " +
				"until the next user interaction triggers a fresh flush.");

#if DEBUG
			throw new InvalidOperationException(
				$"Reactive graph cycle detected: exceeded {MaxFlushDepth} flush iterations. " +
				"Check for effects that write signals consumed by other effects in a loop.");
#endif

			lock (_lock)
			{
				_dirtyEffects.Clear();
				_dirtyViews.Clear();
			}
			return;
		}

		Effect[] effects;
		CometView[] views;

		lock (_lock)
		{
			effects = _dirtyEffects.Count > 0
				? _dirtyEffects.ToArray()
				: Array.Empty<Effect>();
			_dirtyEffects.Clear();

			views = _dirtyViews.Count > 0
				? _dirtyViews.ToArray()
				: Array.Empty<CometView>();
			_dirtyViews.Clear();
		}

		foreach (var effect in effects)
			effect.Flush();

		foreach (var view in views)
		{
			if (!view.IsDisposed)
			{
				view.Reload();
				ReactiveDiagnostics.NotifyViewRebuilt(view, trigger: null);
			}
		}

		bool hasMore;
		lock (_lock)
		{
			hasMore = _dirtyEffects.Count > 0 || _dirtyViews.Count > 0;
		}

		if (hasMore)
			Flush(depth + 1);
	}

	public static void FlushSync()
	{
		if (Application.Current?.Dispatcher is { } dispatcher && dispatcher.IsDispatchRequired)
		{
			throw new InvalidOperationException(
				"FlushSync must be called on the UI thread. Background-thread mutations " +
				"should rely on the automatic dispatcher-posted flush.");
		}

		lock (_lock)
		{
			_flushScheduled = false;
		}

		Flush(depth: 0);
	}
}
