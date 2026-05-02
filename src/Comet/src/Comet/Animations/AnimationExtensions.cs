using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Animations;

namespace Comet
{
	public static class AnimationExtensions
	{
		public static FrameConstraints Lerp(this FrameConstraints start, FrameConstraints end, double progress)
			=> new FrameConstraints(
				start.Width.Lerp(end.Width, progress),
				start.Height.Lerp(end.Height, progress)
				);
		public static T Animate<T>(this T view, Action<T> action, Action completed = null, double duration = .2, double delay = 0, bool repeats = false, bool autoReverses = false, string id = null, Lerp lerp = null)
			where T : View => view.Animate(Easing.Default, action, completed, duration, delay, repeats, autoReverses, id, lerp);

		public static T Animate<T>(this T view, Easing easing, Action<T> action, Action completed = null, double duration = .2, double delay = 0, bool repeats = false, bool autoReverses = false, string id = null, Lerp lerp = null)
		where T : View
		{
			var animation = CreateAnimation(view, easing, action, completed, duration, delay, repeats, autoReverses, id, lerp);
			view.AddAnimation(animation);
			return view;
		}

		public static AnimationSequence<T> BeginAnimationSequence<T>(this T view, Action completed = null, double delay = 0, bool repeats = false, string id = null)
			where T : View
				=> new AnimationSequence<T>(view)
				{
					StartDelay = delay,
					Repeats = repeats,
					Id = id,
				};

		public static Animation CreateAnimation<T>(T view, Easing easing, Action<T> action, Action completed = null, double duration = .2, double delay = 0, bool repeats = false, bool autoReverses = false, string id = null, Lerp lerp = null)
			where T : View
		{
			ContextualObject.MonitorChanges();
			action(view);
			var changedProperties = ContextualObject.StopMonitoringChanges();
			List<Animation> animations = null;
			if (changedProperties.Count == 0)
				return null;

			if (changedProperties.Count > 1)
				animations = new List<Animation>();

			foreach (var change in changedProperties)
			{
				var prop = change.Key;

				var values = change.Value;
				//Handle the bingings!
				if (values.newValue == values.oldValue)
					continue;

				// When a property has never been set, oldValue is null.
				// LerpingAnimation can't interpolate from null, so default
				// to the numeric zero for the matching type.
				var startValue = values.oldValue;
				if (startValue is null)
				{
					startValue = values.newValue switch
					{
						double => 0.0,
						float => 0.0f,
						int => 0,
						_ => startValue,
					};
				}

				Animation animation = new ContextualAnimation
				{
					Duration = duration,
					Easing = easing,
					Repeats = repeats,
					StartDelay = delay,
					StartValue = startValue,
					EndValue = values.newValue,
					ContextualObject = prop.view,
					PropertyName = prop.property,
					Id = id,
					Lerp = lerp,
					
				};
				if (autoReverses)
					animation = animation.CreateAutoReversing();
				if (animations is null)
					return animation;
				animations.Add(animation);
			}

			return new ContextualAnimation(animations)
			{
				Id = id,
				Duration = duration,
				Easing = easing,
				Repeats = repeats,
			};
		}

		public static void AbortAnimation<T>(this T view, string id) where T : View
		{
			view.RemoveAnimation(id);
		}

		public static T FadeTo<T>(this T view, double opacity, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v => v.Opacity(opacity), duration: duration);
		}

		public static T TranslateTo<T>(this T view, double x, double y, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v =>
			{
				v.TranslationX(x);
				v.TranslationY(y);
			}, duration: duration);
		}

		public static T ScaleTo<T>(this T view, double scale, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v => v.Scale(scale), duration: duration);
		}

		public static T ScaleTo<T>(this T view, double scaleX, double scaleY, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v =>
			{
				v.ScaleX(scaleX);
				v.ScaleY(scaleY);
			}, duration: duration);
		}

		public static T RotateTo<T>(this T view, double rotation, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v => v.Rotation(rotation), duration: duration);
		}

		public static T RotateXTo<T>(this T view, double rotation, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v => v.RotationX(rotation), duration: duration);
		}

		public static T RotateYTo<T>(this T view, double rotation, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v => v.RotationY(rotation), duration: duration);
		}

		public static T ColorTo<T>(this T view, Color targetColor, double duration = 0.25, Easing easing = null) where T : View
		{
			return view.Animate(easing ?? Easing.Default, v => v.Background(targetColor), duration: duration);
		}

		// --- Fluent AnimationBuilder ---

		/// <summary>
		/// Compose complex animations using a fluent builder with sequences, parallel groups, and delays.
		/// Duration and delay values in the builder are specified in milliseconds.
		/// </summary>
		public static T Animate<T>(this T view, Action<AnimationBuilder<T>> configure) where T : View
		{
			var builder = new AnimationBuilder<T>(view);
			configure(builder);
			builder.Build();
			return view;
		}

		// --- Spring Animations ---

		/// <summary>
		/// Animate properties using spring physics with a preset configuration.
		/// </summary>
		public static T Spring<T>(this T view, Action<T> action, SpringPreset preset, string id = null, Lerp lerp = null) where T : View
		{
			return view.Spring(action, new SpringAnimation(preset), id, lerp);
		}

		/// <summary>
		/// Animate properties using spring physics with custom mass, stiffness, and damping.
		/// </summary>
		public static T Spring<T>(this T view, Action<T> action, double mass = 1, double stiffness = 100, double damping = 10, string id = null, Lerp lerp = null) where T : View
		{
			return view.Spring(action, new SpringAnimation(mass, stiffness, damping), id, lerp);
		}

		private static T Spring<T>(this T view, Action<T> action, SpringAnimation template, string id, Lerp lerp) where T : View
		{
			ContextualObject.MonitorChanges();
			action(view);
			var changedProperties = ContextualObject.StopMonitoringChanges();

			if (changedProperties.Count == 0)
				return view;

			List<Animation> animations = null;
			if (changedProperties.Count > 1)
				animations = new List<Animation>();

			foreach (var change in changedProperties)
			{
				var prop = change.Key;
				var values = change.Value;

				if (Equals(values.newValue, values.oldValue))
					continue;

				var spring = new SpringAnimation
				{
					Mass = template.Mass,
					Stiffness = template.Stiffness,
					DampingCoefficient = template.DampingCoefficient,
					InitialVelocity = template.InitialVelocity,
					StartValue = values.oldValue,
					EndValue = values.newValue,
					ContextualObject = prop.view,
					PropertyName = prop.property,
					PropertyCascades = prop.cascades,
					Id = id,
					Lerp = lerp,
				};

				if (animations is null)
				{
					view.AddAnimation(spring);
					return view;
				}
				animations.Add(spring);
			}

			if (animations is not null && animations.Count > 0)
			{
				var group = new ContextualAnimation(animations)
				{
					Id = id,
					Duration = template.EstimateDuration(),
				};
				view.AddAnimation(group);
			}
			return view;
		}

		// --- Keyframe Animations ---

		/// <summary>
		/// Animate through keyframes defined at specific progress points (0.0 to 1.0).
		/// Duration is in milliseconds.
		/// </summary>
		public static T Keyframes<T>(this T view, Action<KeyframeBuilder<T>> configure, double duration = 600, Easing easing = null) where T : View
		{
			var builder = new KeyframeBuilder<T>();
			configure(builder);

			if (builder.Keyframes.Count < 2)
				return view;

			var animation = new KeyframeAnimation<T>(view, builder.Keyframes, duration / 1000.0, easing);
			view.AddAnimation(animation);
			return view;
		}
	}
}
