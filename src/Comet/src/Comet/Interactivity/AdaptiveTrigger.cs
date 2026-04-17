using System;
using System.Collections.Generic;

namespace Comet
{
	/// <summary>
	/// A trigger that fires based on window size thresholds.
	/// Used for responsive layouts in MVU. Prefer <see cref="AdaptiveTrigger{T}"/>
	/// for functional (non-setter) responsive adjustments.
	/// </summary>
	public class AdaptiveTrigger : DataTrigger
	{
		public double MinWindowWidth { get; set; } = -1;
		public double MinWindowHeight { get; set; } = -1;

		public AdaptiveTrigger() { }

		public AdaptiveTrigger(double minWindowWidth = -1, double minWindowHeight = -1)
		{
			MinWindowWidth = minWindowWidth;
			MinWindowHeight = minWindowHeight;
		}

		/// <summary>
		/// Evaluates whether the current window dimensions meet the trigger thresholds.
		/// </summary>
		public bool Evaluate(double currentWidth, double currentHeight)
		{
			bool widthMet = MinWindowWidth < 0 || currentWidth >= MinWindowWidth;
			bool heightMet = MinWindowHeight < 0 || currentHeight >= MinWindowHeight;
			return widthMet && heightMet;
		}

		/// <summary>
		/// Evaluates the trigger against the provided dimensions. The typed
		/// subclass <see cref="AdaptiveTrigger{T}"/> is the preferred way to
		/// apply size-based changes — it uses functional setters instead of
		/// the legacy <c>Setter</c> collection (removed in Phase 1).
		/// </summary>
		public void Apply(double currentWidth, double currentHeight)
		{
			if (AssociatedObject is null)
				return;

			// Base untyped trigger no longer carries setters; consumers should
			// use AdaptiveTrigger<T> with functional setter/undoSetter.
			_ = Evaluate(currentWidth, currentHeight);
		}
	}

	/// <summary>
	/// A typed adaptive trigger that applies actions based on window size.
	/// More MVU-friendly than the setter-based approach:
	///   myView.AddTrigger(new AdaptiveTrigger&lt;VStack&gt;(
	///       minWindowWidth: 720,
	///       setter: v =&gt; v.FrameConstraints(width: 600),
	///       undoSetter: v =&gt; v.FrameConstraints(width: 300)
	///   ))
	/// </summary>
	public class AdaptiveTrigger<T> : DataTrigger<T> where T : View
	{
		public double MinWindowWidth { get; set; } = -1;
		public double MinWindowHeight { get; set; } = -1;

		public AdaptiveTrigger() { }

		public AdaptiveTrigger(double minWindowWidth = -1, double minWindowHeight = -1,
			Action<T> setter = null, Action<T> undoSetter = null)
		{
			MinWindowWidth = minWindowWidth;
			MinWindowHeight = minWindowHeight;
			Condition = () => EvaluateSize();
			TypedSetter = setter;
			UndoSetter = undoSetter;
		}

		double _currentWidth;
		double _currentHeight;

		bool EvaluateSize()
		{
			bool widthMet = MinWindowWidth < 0 || _currentWidth >= MinWindowWidth;
			bool heightMet = MinWindowHeight < 0 || _currentHeight >= MinWindowHeight;
			return widthMet && heightMet;
		}

		/// <summary>
		/// Updates the current window dimensions and re-evaluates the trigger.
		/// </summary>
		public void UpdateWindowSize(double width, double height)
		{
			_currentWidth = width;
			_currentHeight = height;
			Evaluate();
		}
	}
}
