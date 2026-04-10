using System;
using System.Collections.Generic;

namespace Comet.Reactive;

/// <summary>
/// Callback invoked when a <see cref="PropertySubscription{T}"/>'s value changes.
/// Defined as a named delegate for use in source-generator templates (Phase 2).
/// </summary>
public delegate void PropertyChangedCallback<in T>(T newValue);

/// <summary>
/// Non-generic interface enabling <see cref="ReactiveScheduler"/> to flush
/// property subscriptions without knowing the generic type parameter.
/// Will be wired into the scheduler's flush loop in Phase 2.
/// </summary>
internal interface IPropertySubscriptionFlushable
{
	void Flush();
}

/// <summary>
/// A unified reactive property primitive that uses <see cref="ReactiveScope"/> for
/// automatic dependency tracking and dispatches per-property handler updates when
/// dependencies change.
/// </summary>
/// <remarks>
/// <para>Three construction modes:</para>
/// <list type="bullet">
///   <item><description><c>PropertySubscription(T)</c> — static value, no tracking.</description></item>
///   <item><description><c>PropertySubscription(Func&lt;T&gt;)</c> — evaluates the function,
///     tracks reads via <see cref="ReactiveScope"/>, re-evaluates on change.</description></item>
///   <item><description><c>PropertySubscription(Signal&lt;T&gt;)</c> — bidirectional binding
///     that reads/writes the signal's value.</description></item>
/// </list>
/// <para>
/// <c>Evaluate()</c> creates a NESTED <see cref="ReactiveScope"/> inside whatever scope
/// is active (typically the body scope). This ensures property-level reads are isolated
/// from body-level dependency tracking.
/// </para>
/// </remarks>
public sealed class PropertySubscription<T> : IReactiveSubscriber, IPropertySubscriptionFlushable, IDisposable
{
	readonly Func<T>? _compute;
	readonly EqualityComparer<T> _comparer;
	HashSet<IReactiveSource>? _dependencies;
	T _currentValue = default!;
	bool _dirty;
	bool _disposed;

	WeakReference<View>? _viewRef;
	string? _propertyName;

	/// <summary>Callback fired when the evaluated value changes.</summary>
	public Action<T>? PropertyChangedCallback { get; set; }

	/// <summary>Write-back delegate for two-way (Signal) bindings. Null for static/Func subscriptions.</summary>
	public Action<T>? WriteBack { get; internal set; }

	/// <summary>Current evaluated value.</summary>
	public T Value => _currentValue;

	/// <summary>Current evaluated value (compatible with Binding&lt;T&gt; API).</summary>
	public T CurrentValue => _currentValue;

	/// <summary>Whether this is a static (non-reactive) subscription.</summary>
	public bool IsStatic => _compute is null;

	/// <summary>Whether this wraps a <see cref="Signal{T}"/> for bidirectional binding.</summary>
	public bool IsBidirectional => WriteBack is not null;

	/// <summary>
	/// Sets the current value directly and writes back to the source Signal if bidirectional.
	/// Used by generated interface property implementations (e.g., ISlider.Value setter).
	/// Suppresses reactive notifications during writeback to prevent view rebuilds that
	/// would cause focus loss on text inputs and interaction interruption on sliders/toggles.
	/// </summary>
	public void Set(T value)
	{
		_currentValue = value;
		if (WriteBack is not null)
		{
			ReactiveScheduler.SuppressNotifications = true;
			try
			{
				WriteBack(value);
			}
			finally
			{
				ReactiveScheduler.SuppressNotifications = false;
			}
		}
	}

	/// <summary>
	/// Sets the current value and notifies the bound view if the value changed.
	/// Used by State&lt;T&gt; bridging to propagate changes from the legacy notification system.
	/// </summary>
	internal void SetAndNotify(T value)
	{
		var old = _currentValue;
		_currentValue = value;
		if (!_comparer.Equals(old, _currentValue))
		{
			PropertyChangedCallback?.Invoke(_currentValue);
			if (_viewRef?.TryGetTarget(out var view) == true && _propertyName is not null)
				view.ViewPropertyChanged(_propertyName, _currentValue);
		}
	}

	/// <summary>Static value constructor — no reactive tracking.</summary>
	public PropertySubscription(T value)
	{
		_currentValue = value;
		_comparer = EqualityComparer<T>.Default;
	}

	/// <summary>
	/// Func constructor — evaluates <paramref name="compute"/>, tracks
	/// <see cref="IReactiveSource"/> reads via <see cref="ReactiveScope"/>,
	/// and re-evaluates when any tracked dependency changes.
	/// </summary>
	public PropertySubscription(Func<T> compute, EqualityComparer<T>? comparer = null)
	{
		_compute = compute ?? throw new ArgumentNullException(nameof(compute));
		_comparer = comparer ?? EqualityComparer<T>.Default;
		Evaluate();
	}

	/// <summary>
	/// Signal constructor — bidirectional binding. Reads <see cref="Signal{T}.Value"/>
	/// for display; <see cref="WriteBack"/> writes to the signal from user input.
	/// </summary>
	public PropertySubscription(Signal<T> signal, EqualityComparer<T>? comparer = null)
	{
		if (signal is null) throw new ArgumentNullException(nameof(signal));
		_compute = () => signal.Value;
		WriteBack = v => signal.Value = v;
		_comparer = comparer ?? EqualityComparer<T>.Default;
		Evaluate();
	}

	/// <summary>
	/// Binds this subscription to a <see cref="View"/> and property name so that
	/// value changes dispatch to <see cref="View.ViewPropertyChanged"/>.
	/// Called by <see cref="DatabindingExtensions.SetPropertySubscription{T}"/>.
	/// </summary>
	internal void BindToView(View view, string propertyName)
	{
		_viewRef = new WeakReference<View>(view);
		_propertyName = propertyName;
	}

	/// <summary>
	/// Evaluates the compute function inside a nested <see cref="ReactiveScope"/>,
	/// diffs the old and new dependency sets, and updates subscriptions.
	/// </summary>
	void Evaluate()
	{
		if (_disposed || _compute is null)
			return;

		var oldDeps = _dependencies;
		_dirty = false;

		using var scope = ReactiveScope.BeginTracking();
		T newValue;
		HashSet<IReactiveSource> newDeps;
		try
		{
			newValue = _compute();
		}
		catch
		{
			scope.EndTracking();
			_dirty = true;
			return;
		}

		newDeps = scope.EndTracking();

		// Diff subscriptions: unsubscribe from removed deps
		if (oldDeps is not null)
		{
			foreach (var dep in oldDeps)
			{
				if (!newDeps.Contains(dep))
					dep.Unsubscribe(this);
			}
		}

		// Subscribe to newly discovered deps
		foreach (var dep in newDeps)
		{
			if (oldDeps is null || !oldDeps.Contains(dep))
				dep.Subscribe(this);
		}

		_dependencies = newDeps;
		_currentValue = newValue;
	}

	/// <summary>
	/// Called by the reactive infrastructure when a tracked dependency changes.
	/// Re-evaluates the expression and fires the callback if the value changed.
	/// </summary>
	public void OnDependencyChanged(IReactiveSource source)
	{
		if (_dirty || _disposed)
			return;

		_dirty = true;

		var oldValue = _currentValue;
		Evaluate();

		if (!_comparer.Equals(oldValue, _currentValue))
		{
			PropertyChangedCallback?.Invoke(_currentValue);

			if (_viewRef?.TryGetTarget(out var view) == true && _propertyName is not null)
				view.ViewPropertyChanged(_propertyName, _currentValue);
		}
	}

	/// <summary>
	/// Processes any pending dirty state. Called by <see cref="ReactiveScheduler"/>
	/// during flush (Phase 2), or manually for testing.
	/// </summary>
	public void Flush()
	{
		if (!_dirty || _disposed)
			return;

		var oldValue = _currentValue;
		Evaluate();

		if (!_comparer.Equals(oldValue, _currentValue))
		{
			PropertyChangedCallback?.Invoke(_currentValue);

			if (_viewRef?.TryGetTarget(out var view) == true && _propertyName is not null)
				view.ViewPropertyChanged(_propertyName, _currentValue);
		}
	}

	/// <summary>Explicit interface implementation for scheduler integration.</summary>
	void IPropertySubscriptionFlushable.Flush() => Flush();

	/// <summary>
	/// Unsubscribes from all tracked dependencies and releases resources.
	/// </summary>
	public void Dispose()
	{
		_disposed = true;
		if (_dependencies is not null)
		{
			foreach (var dep in _dependencies)
				dep.Unsubscribe(this);
			_dependencies = null;
		}
	}

	/// <summary>Creates a static (non-reactive) subscription wrapping the given value.</summary>
	public static PropertySubscription<T> FromValue(T value) => new(value);

	/// <summary>Creates a reactive subscription that evaluates the given function and tracks dependencies.</summary>
	public static PropertySubscription<T> FromFunc(Func<T> compute) => new(compute);

	/// <summary>Creates a bidirectional subscription bound to the given signal.</summary>
	public static PropertySubscription<T> FromSignal(Signal<T> signal) => new(signal);

	/// <summary>Implicit conversion from a static value (API-compatible replacement for Binding&lt;T&gt; implicit).</summary>
	public static implicit operator PropertySubscription<T>(T value) => new(value);

	/// <summary>Implicit conversion from a Func (API-compatible replacement for Binding&lt;T&gt; implicit).</summary>
	public static implicit operator PropertySubscription<T>(Func<T> compute) => compute is null ? null! : new(compute);

	/// <summary>Implicit conversion from Reactive&lt;T&gt; for backward compatibility during migration.
	/// Subscribes to Reactive.ValueChanged to bridge notifications to PropertySubscription.</summary>
	public static implicit operator PropertySubscription<T>(Reactive<T> reactive)
	{
		if (reactive is null) return null!;
		var sub = new PropertySubscription<T>(reactive.Value);
		reactive.ValueChanged += v => sub.SetAndNotify(v);
		sub.WriteBack = v => reactive.Value = v;
		return sub;
	}

	/// <summary>Implicit conversion to <typeparamref name="T"/> for convenient value access.</summary>
	public static implicit operator T(PropertySubscription<T>? sub) =>
		sub is null ? default! : sub._currentValue;
}
