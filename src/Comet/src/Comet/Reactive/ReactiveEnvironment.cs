using System.Collections.Generic;

namespace Comet.Reactive;

internal sealed class ReactiveEnvironment
{
	readonly object _lock = new();
	readonly Dictionary<string, EnvironmentKeySource> _sources = new();

	public void SetValue(string key, object value)
	{
		GetSource(key).NotifyChanged();
		ReactiveScheduler.EnsureFlushScheduled();
	}

	public void TrackRead(string key)
	{
		ReactiveScope.Current?.TrackRead(GetSource(key));
	}

	EnvironmentKeySource GetSource(string key)
	{
		lock (_lock)
		{
			if (_sources.TryGetValue(key, out var source))
				return source;

			source = new EnvironmentKeySource(key);
			_sources.Add(key, source);
			return source;
		}
	}

	sealed class EnvironmentKeySource : IReactiveSource
	{
		readonly SubscriberList _subscribers = new();
		uint _version;

		public EnvironmentKeySource(string key)
		{
			Key = key;
		}

		public string Key { get; }

		public uint Version => _version;

		public void NotifyChanged()
		{
			unchecked { _version++; }
			_subscribers.NotifyAll(this);
		}

		public void Subscribe(IReactiveSubscriber subscriber) => _subscribers.Add(subscriber);

		public void Unsubscribe(IReactiveSubscriber subscriber) => _subscribers.Remove(subscriber);
	}
}
