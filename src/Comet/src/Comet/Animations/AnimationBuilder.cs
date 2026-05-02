using System;
using System.Collections.Generic;
using Microsoft.Maui;

namespace Comet
{
	/// <summary>
	/// Fluent builder for composing complex animations with sequences, parallel groups, and delays.
	/// Duration and delay values are specified in milliseconds.
	/// </summary>
	public class AnimationBuilder<T> where T : View
	{
		private readonly T _view;
		private readonly List<BuilderStep> _steps = new();
		private Easing _defaultEasing;

		internal AnimationBuilder(T view)
		{
			_view = view;
		}

		/// <summary>Animate opacity to the target value.</summary>
		public AnimationBuilder<T> FadeTo(double opacity, double duration = 250)
		{
			_steps.Add(new BuilderStep { Action = v => v.Opacity(opacity), DurationMs = duration });
			return this;
		}

		/// <summary>Animate translation to the target position.</summary>
		public AnimationBuilder<T> TranslateTo(double x, double y, double duration = 250)
		{
			_steps.Add(new BuilderStep
			{
				Action = v => { v.TranslationX(x); v.TranslationY(y); },
				DurationMs = duration
			});
			return this;
		}

		/// <summary>Animate uniform scale to the target value.</summary>
		public AnimationBuilder<T> ScaleTo(double scale, double duration = 250)
		{
			_steps.Add(new BuilderStep { Action = v => v.Scale(scale), DurationMs = duration });
			return this;
		}

		/// <summary>Animate rotation to the target value in degrees.</summary>
		public AnimationBuilder<T> RotateTo(double rotation, double duration = 250)
		{
			_steps.Add(new BuilderStep { Action = v => v.Rotation(rotation), DurationMs = duration });
			return this;
		}

		/// <summary>
		/// Adds a sequential animation group that runs after any preceding steps.
		/// </summary>
		public AnimationBuilder<T> Then(Action<AnimationBuilder<T>> configure)
		{
			var sub = new AnimationBuilder<T>(_view) { _defaultEasing = _defaultEasing };
			configure(sub);
			_steps.AddRange(sub._steps);
			return this;
		}

		/// <summary>
		/// Runs multiple animation groups simultaneously as a single step.
		/// </summary>
		public AnimationBuilder<T> Parallel(params Action<AnimationBuilder<T>>[] animations)
		{
			var actions = new List<Action<T>>();
			double maxDuration = 250;

			foreach (var configure in animations)
			{
				var sub = new AnimationBuilder<T>(_view);
				configure(sub);
				foreach (var step in sub._steps)
				{
					actions.Add(step.Action);
					maxDuration = Math.Max(maxDuration, step.DurationMs);
				}
			}

			var capturedActions = actions.ToArray();
			_steps.Add(new BuilderStep
			{
				Action = v => { foreach (var a in capturedActions) a(v); },
				DurationMs = maxDuration,
			});
			return this;
		}

		/// <summary>Sets a delay in milliseconds before the last added step starts.</summary>
		public AnimationBuilder<T> WithDelay(double milliseconds)
		{
			if (_steps.Count > 0)
				_steps[_steps.Count - 1].DelayMs = milliseconds;
			return this;
		}

		/// <summary>Sets the easing function for the last added step, or the default easing if no steps exist.</summary>
		public AnimationBuilder<T> WithEasing(Easing easing)
		{
			if (_steps.Count > 0)
				_steps[_steps.Count - 1].Easing = easing;
			else
				_defaultEasing = easing;
			return this;
		}

		internal void Build()
		{
			if (_steps.Count == 0)
				return;

			if (_steps.Count == 1)
			{
				var step = _steps[0];
				_view.Animate(
					step.Easing ?? _defaultEasing ?? Easing.Default,
					step.Action,
					duration: step.DurationMs / 1000.0,
					delay: step.DelayMs / 1000.0);
				return;
			}

			var sequence = _view.BeginAnimationSequence();
			foreach (var step in _steps)
			{
				sequence.Animate(
					step.Easing ?? _defaultEasing ?? Easing.Default,
					step.Action,
					duration: step.DurationMs / 1000.0,
					delay: step.DelayMs / 1000.0);
			}
			sequence.EndAnimationSequence();
		}

		private class BuilderStep
		{
			public Action<T> Action { get; set; }
			public double DurationMs { get; set; }
			public double DelayMs { get; set; }
			public Easing Easing { get; set; }
		}
	}
}

namespace Comet.Animations
{
	/// <summary>
	/// Animation helper methods for common patterns.
	/// </summary>
	public static class AnimationHelpers
	{
		/// <summary>Create a fade-in animation.</summary>
		public static async void AnimateFadeIn(this View view, Action onComplete = null)
		{
			view.Opacity(0);
			await System.Threading.Tasks.Task.Delay(50);
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
			{
				view.Opacity(1);
				onComplete?.Invoke();
			});
		}

		/// <summary>Create a fade-out animation.</summary>
		public static async void AnimateFadeOut(this View view, Action onComplete = null)
		{
			view.Opacity(1);
			await System.Threading.Tasks.Task.Delay(50);
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
			{
				view.Opacity(0);
				onComplete?.Invoke();
			});
		}

		/// <summary>Pulse animation - fades in and out repeatedly.</summary>
		public static async void AnimatePulse(this View view, int count = 1)
		{
			for (int i = 0; i < count; i++)
			{
				view.Opacity(0.5);
				await System.Threading.Tasks.Task.Delay(200);

				view.Opacity(1);
				await System.Threading.Tasks.Task.Delay(200);
			}
		}
	}
}
