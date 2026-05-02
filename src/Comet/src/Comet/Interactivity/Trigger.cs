using System;

namespace Comet
{
	/// <summary>
	/// A typed data trigger that evaluates a condition and applies actions.
	/// In MVU, triggers use functional patterns:
	///   myButton.AddTrigger(new DataTrigger&lt;Button&gt;(
	///       condition: () =&gt; isEnabled,
	///       setter: btn =&gt; btn.BackgroundColor(Colors.Blue),
	///       undoSetter: btn =&gt; btn.BackgroundColor(Colors.Gray)
	///   ))
	/// </summary>
	public class DataTrigger<T> : DataTrigger where T : View
	{
		public Func<bool> Condition { get; set; }
		public Action<T> TypedSetter { get; set; }
		public Action<T> UndoSetter { get; set; }

		public DataTrigger() { }

		public DataTrigger(Func<bool> condition, Action<T> setter, Action<T> undoSetter = null)
		{
			Condition = condition;
			TypedSetter = setter;
			UndoSetter = undoSetter;
		}

		/// <summary>
		/// Evaluates the condition and applies the appropriate setter or undo setter.
		/// </summary>
		public void Evaluate()
		{
			if (AssociatedObject is not T typedView)
				return;

			if (Condition?.Invoke() == true)
				TypedSetter?.Invoke(typedView);
			else
				UndoSetter?.Invoke(typedView);
		}
	}

	/// <summary>
	/// A typed event trigger for MVU patterns.
	///   myView.AddTrigger(new EventTrigger&lt;Button&gt;("Clicked",
	///       action: btn =&gt; btn.BackgroundColor(Colors.Red)
	///   ))
	/// </summary>
	public class EventTrigger<T> : EventTrigger where T : View
	{
		public Action<T> TypedAction { get; set; }

		public EventTrigger() { }

		public EventTrigger(string eventName, Action<T> action)
		{
			Event = eventName;
			TypedAction = action;
		}

		/// <summary>
		/// Invokes both the typed action and the base action.
		/// </summary>
		public void Invoke()
		{
			if (AssociatedObject is T typedView)
				TypedAction?.Invoke(typedView);
			Action?.Invoke();
		}
	}

	/// <summary>
	/// A trigger that fires based on a boolean state value.
	/// Simpler than DataTrigger when working with State&lt;bool&gt; in MVU.
	/// </summary>
	public class StateTrigger : DataTrigger
	{
		public bool IsActive { get; set; }
	}
}
