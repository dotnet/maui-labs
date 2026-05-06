using System;
using Comet.Reactive;

namespace Comet
{
	internal class MulticastAction<T>
	{
		Action<T> action;
		PropertySubscription<T> subscription;
		public MulticastAction(PropertySubscription<T> subscription, Action<T> action)
		{
			this.subscription = subscription;
			this.action = action;
		}

		public void Invoke(T value)
		{
			subscription.Set(value);
			action?.Invoke(value);
		}

		public static implicit operator Action<T>(MulticastAction<T> action) => action.Invoke;
	}
}
