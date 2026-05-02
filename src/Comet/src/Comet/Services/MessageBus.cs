using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// Lightweight pub/sub messaging system for decoupled communication between views.
	/// Uses weak references to prevent memory leaks.
	/// Matches MAUI's MessagingCenter/WeakReferenceMessenger pattern.
	/// </summary>
	public static class MessageBus
	{
		static readonly ConcurrentDictionary<string, List<WeakSubscription>> _subscriptions = new();

		/// <summary>
		/// Subscribe to a typed message.
		/// </summary>
		public static void Subscribe<TMessage>(object subscriber, string message, Action<object, TMessage> callback)
		{
			var subs = _subscriptions.GetOrAdd(message, _ => new List<WeakSubscription>());
			lock (subs)
			{
				subs.Add(new WeakSubscription(subscriber, (sender, args) => callback(sender, (TMessage)args)));
			}
		}

		/// <summary>
		/// Subscribe to a message without args.
		/// </summary>
		public static void Subscribe(object subscriber, string message, Action<object> callback)
		{
			var subs = _subscriptions.GetOrAdd(message, _ => new List<WeakSubscription>());
			lock (subs)
			{
				subs.Add(new WeakSubscription(subscriber, (sender, _) => callback(sender)));
			}
		}

		/// <summary>
		/// Send a typed message to all subscribers.
		/// </summary>
		public static void Send<TMessage>(object sender, string message, TMessage args)
		{
			if (!_subscriptions.TryGetValue(message, out var subs))
				return;

			List<WeakSubscription> toInvoke;
			lock (subs)
			{
				subs.RemoveAll(s => !s.IsAlive);
				toInvoke = new List<WeakSubscription>(subs);
			}

			foreach (var sub in toInvoke)
				sub.Invoke(sender, args);
		}

		/// <summary>
		/// Send a message without args.
		/// </summary>
		public static void Send(object sender, string message) => Send<object>(sender, message, null);

		/// <summary>
		/// Unsubscribe from a specific message.
		/// </summary>
		public static void Unsubscribe(object subscriber, string message)
		{
			if (_subscriptions.TryGetValue(message, out var subs))
				lock (subs) { subs.RemoveAll(s => !s.IsAlive || s.Matches(subscriber)); }
		}

		/// <summary>
		/// Unsubscribe from all messages.
		/// </summary>
		public static void UnsubscribeAll(object subscriber)
		{
			foreach (var kvp in _subscriptions)
				lock (kvp.Value) { kvp.Value.RemoveAll(s => !s.IsAlive || s.Matches(subscriber)); }
		}

		/// <summary>Clear all subscriptions (for testing).</summary>
		public static void Reset() => _subscriptions.Clear();

		class WeakSubscription
		{
			readonly WeakReference _ref;
			readonly Action<object, object> _callback;

			public WeakSubscription(object subscriber, Action<object, object> callback)
			{
				_ref = new WeakReference(subscriber);
				_callback = callback;
			}

			public bool IsAlive => _ref.IsAlive;
			public bool Matches(object subscriber) => ReferenceEquals(_ref.Target, subscriber);
			public void Invoke(object sender, object args) { if (_ref.IsAlive) _callback?.Invoke(sender, args); }
		}
	}
}
