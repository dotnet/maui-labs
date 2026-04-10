using System;

namespace Comet
{
	public abstract class Behavior
	{
		protected internal View AssociatedObject { get; private set; }

		internal void Attach(View view)
		{
			if (AssociatedObject is not null)
				throw new InvalidOperationException("Behavior is already attached to a view");

			AssociatedObject = view;
			OnAttachedTo(view);
		}

		internal void Detach()
		{
			if (AssociatedObject is null)
				return;

			OnDetachingFrom(AssociatedObject);
			AssociatedObject = null;
		}

		protected virtual void OnAttachedTo(View view) { }
		protected virtual void OnDetachingFrom(View view) { }
	}

	public abstract class Behavior<T> : Behavior where T : View
	{
		protected T TypedAssociatedObject => AssociatedObject as T;

		protected sealed override void OnAttachedTo(View view)
		{
			base.OnAttachedTo(view);
			if (view is T typedView)
				OnAttachedTo(typedView);
		}

		protected sealed override void OnDetachingFrom(View view)
		{
			base.OnDetachingFrom(view);
			if (view is T typedView)
				OnDetachingFrom(typedView);
		}

		protected virtual void OnAttachedTo(T view) { }
		protected virtual void OnDetachingFrom(T view) { }
	}
}
