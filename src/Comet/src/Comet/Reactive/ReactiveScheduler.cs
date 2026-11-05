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

	/// <summary>Depth of active <see cref="HoldFlushes"/> scopes on this thread.</summary>
	[ThreadStatic]
	static int _holdDepth;

	/// <summary>
	/// Defers reactive flushes for the duration of the returned scope. An own-content node
	/// swapping its hosted subtree materializes views whose modifiers write the environment;
	/// each write schedules a flush, and on the UI thread the flush runs INLINE — its
	/// AfterFlush layout pass would then re-arrange the node that is still mid-build and
	/// re-enter the swap (double-built subtrees, lost node state). Holding makes the swap
	/// atomic: writes just mark the flush pending, and it runs once when the scope closes.
	/// </summary>
	public static IDisposable HoldFlushes()
	{
		_holdDepth++;
		return new FlushHold();
	}

	sealed class FlushHold : IDisposable
	{
		bool _released;
		public void Dispose()
		{
			if (_released)
				return;
			_released = true;
			if (--_holdDepth == 0 && _flushScheduled)
			{
				// Re-drive scheduling for the pending work that accumulated during the hold.
				lock (_lock)
				{
					_flushScheduled = false;
				}
				EnsureFlushScheduled();
			}
		}
	}

	public static void EnsureFlushScheduled()
	{
		// If a flush is already in progress, skip scheduling — the current
		// Flush loop's hasMore check will pick up newly-dirtied views/effects.
		// This prevents StackOverflow when Reload → SetEnvironment → NotifyChanged
		// → MarkViewDirty → EnsureFlushScheduled would otherwise re-enter FlushEntry.
		if (_flushScheduled || _flushing)
			return;

		// A HoldFlushes scope is active on this thread: record that a flush is wanted and
		// let the scope run it on release (keeps hosted-subtree swaps atomic).
		if (_holdDepth > 0)
		{
			lock (_lock)
			{
				_flushScheduled = true;
			}
			return;
		}

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

	/// <summary>
	/// Raised on the UI thread after a full flush cycle settles (effects + view reloads + their
	/// backend property pushes are done). The layout-driving backends subscribe to this to
	/// recompute Yoga layout once per flush, so reactive content-size changes reflow.
	/// </summary>
	public static event Action? AfterFlush;

	/// <summary>True while <see cref="FlushEntry"/> is running on this thread — including its
	/// <see cref="AfterFlush"/> phase, which <see cref="_flushing"/> deliberately excludes.</summary>
	[ThreadStatic]
	static bool _inFlushEntry;

	static void FlushEntry()
	{
		// Re-entrancy guard. An AfterFlush handler that BUILDS views — an own-content node
		// refreshing its hosted subtree during the post-flush layout pass — writes the
		// environment, which calls EnsureFlushScheduled; with _flushing already false that
		// would run a nested FlushEntry inline, whose AfterFlush re-arranges the node that is
		// still mid-build and recurses without bound (the Reply detail-swap stack overflow).
		// Instead leave _flushScheduled set and return: the outer call's pass loop below
		// picks the new work up after the current pass settles.
		if (_inFlushEntry)
			return;

		_inFlushEntry = true;
		try
		{
			for (int pass = 0; ; pass++)
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

				AfterFlush?.Invoke();

				lock (_lock)
				{
					if (!_flushScheduled && _dirtyEffects.Count == 0 && _dirtyViews.Count == 0)
						return;
				}

				if (pass >= MaxFlushDepth)
				{
					ReactiveDiagnostics.NotifyFlushDepthWarning(pass);
					Debug.WriteLine(
						$"[Comet.Reactive] ReactiveScheduler exceeded {MaxFlushDepth} AfterFlush passes. " +
						"This indicates AfterFlush work that re-dirties the graph every pass " +
						"(e.g. a layout handler rebuilding views unconditionally). Breaking the cycle.");
#if DEBUG
					throw new InvalidOperationException(
						$"Reactive AfterFlush cycle detected: exceeded {MaxFlushDepth} passes. " +
						"Check AfterFlush handlers that write signals/environment on every pass.");
#else
					lock (_lock)
					{
						_flushScheduled = false;
						_dirtyEffects.Clear();
						_dirtyViews.Clear();
					}
					return;
#endif
				}
			}
		}
		finally
		{
			_inFlushEntry = false;
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
