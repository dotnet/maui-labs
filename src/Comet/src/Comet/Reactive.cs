using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Comet.Reactive;

namespace Comet
{
	/// <summary>
	/// Forward-facing reactive state wrapper. Standalone implementation that works in
	/// both the classic View/[Body] pattern and the new Component pattern.
	/// Implements <see cref="IReactiveSource"/> so reads inside a <see cref="ReactiveScope"/>
	/// (e.g. a [Body] method) are tracked and writes trigger view rebuilds.
	///
	/// Usage:
	///   readonly Reactive&lt;int&gt; count = 0;        // implicit conversion
	///   count.Value++;                               // triggers re-render
	///   new Text(() =&gt; $"Count: {count.Value}")   // automatic binding
	/// </summary>
	public class Reactive<T> : IReactiveSource, INotifyPropertyRead, IDisposable
	{
		static readonly PropertyChangedEventArgs ValueChangedArgs = new("Value");

		volatile StrongBox<T> _box;
		readonly EqualityComparer<T> _comparer;
		readonly SubscriberList _subscribers = new();
		uint _version;

		public event PropertyChangedEventHandler? PropertyRead;
		public event PropertyChangedEventHandler? PropertyChanged;

		public Reactive() : this(default!) { }

		public Reactive(T value)
		{
			_box = new StrongBox<T>(value);
			_comparer = EqualityComparer<T>.Default;
		}

		/// <summary>
		/// Gets or sets the value. The getter tracks reads inside a
		/// <see cref="ReactiveScope"/> and the setter notifies reactive
		/// subscribers when the value changes.
		/// </summary>
		public T Value
		{
			get
			{
				ReactiveScope.Current?.TrackRead(this);
				PropertyRead?.Invoke(this, ValueChangedArgs);
				return _box.Value;
			}
			set
			{
				if (_comparer.Equals(_box.Value, value))
					return;
				_box = new StrongBox<T>(value);
				unchecked { _version++; }
				_subscribers.NotifyAll(this);
				PropertyChanged?.Invoke(this, ValueChangedArgs);
				ValueChanged?.Invoke(value);
				ReactiveScheduler.EnsureFlushScheduled();
			}
		}

		public uint Version => _version;
		public Action<T>? ValueChanged { get; set; }

		public void Subscribe(IReactiveSubscriber subscriber) => _subscribers.Add(subscriber);
		public void Unsubscribe(IReactiveSubscriber subscriber) => _subscribers.Remove(subscriber);

		public void Dispose()
		{
			_subscribers.Clear();
		}

		public static implicit operator T(Reactive<T> reactive) => reactive.Value;
		public static implicit operator Reactive<T>(T value) => new Reactive<T>(value);

		public override string ToString() => Value?.ToString() ?? string.Empty;
	}
}
