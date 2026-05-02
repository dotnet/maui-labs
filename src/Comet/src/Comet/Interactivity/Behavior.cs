using System;

namespace Comet
{
	/// <summary>
	/// A functional behavior that uses lambda expressions for attach/detach.
	/// Ideal for MVU patterns where behaviors are defined inline.
	/// Usage: myView.AddBehavior(new ActionBehavior(
	///     onAttached: v => { /* setup */ },
	///     onDetaching: v => { /* cleanup */ }
	/// ))
	/// </summary>
	public class ActionBehavior : Behavior
	{
		readonly Action<View> _onAttached;
		readonly Action<View> _onDetaching;

		public ActionBehavior(Action<View> onAttached = null, Action<View> onDetaching = null)
		{
			_onAttached = onAttached;
			_onDetaching = onDetaching;
		}

		protected override void OnAttachedTo(View view) => _onAttached?.Invoke(view);
		protected override void OnDetachingFrom(View view) => _onDetaching?.Invoke(view);
	}

	/// <summary>
	/// A typed functional behavior for MVU patterns.
	/// Usage: myButton.AddBehavior(new ActionBehavior&lt;Button&gt;(
	///     onAttached: btn => { /* setup */ },
	///     onDetaching: btn => { /* cleanup */ }
	/// ))
	/// </summary>
	public class ActionBehavior<T> : Behavior<T> where T : View
	{
		readonly Action<T> _onAttached;
		readonly Action<T> _onDetaching;

		public ActionBehavior(Action<T> onAttached = null, Action<T> onDetaching = null)
		{
			_onAttached = onAttached;
			_onDetaching = onDetaching;
		}

		protected override void OnAttachedTo(T view) => _onAttached?.Invoke(view);
		protected override void OnDetachingFrom(T view) => _onDetaching?.Invoke(view);
	}

	/// <summary>
	/// Base class for validation behaviors in MVU pattern.
	/// Subclass and override Validate to provide custom validation logic.
	/// </summary>
	public abstract class ValidationBehavior<T> : Behavior<T> where T : View
	{
		public bool IsValid { get; private set; }

		protected abstract bool Validate(T view);

		/// <summary>
		/// Forces a re-evaluation of the validation logic.
		/// </summary>
		public void ForceValidation()
		{
			if (TypedAssociatedObject is not null)
				IsValid = Validate(TypedAssociatedObject);
		}
	}
}
