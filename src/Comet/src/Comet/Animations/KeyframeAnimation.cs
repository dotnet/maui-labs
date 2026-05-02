using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Animations;

namespace Comet
{
	/// <summary>
	/// Builder for defining keyframes at specific progress points (0.0 to 1.0).
	/// </summary>
	public class KeyframeBuilder<T> where T : View
	{
		internal readonly List<(double progress, Action<T> action)> Keyframes = new();

		/// <summary>
		/// Defines a keyframe at the given progress (0.0 to 1.0).
		/// </summary>
		public KeyframeBuilder<T> At(double progress, Action<T> action)
		{
			Keyframes.Add((Math.Clamp(progress, 0.0, 1.0), action));
			return this;
		}
	}

	/// <summary>
	/// Animation that transitions through keyframes at specific progress points.
	/// Each segment between keyframes is independently interpolated.
	/// </summary>
	public class KeyframeAnimation<T> : ContextualAnimation where T : View
	{
		private readonly T _view;
		private readonly List<(double progress, Action<T> action)> _sortedKeyframes;
		private readonly double _totalDuration;
		private readonly Easing _segmentEasing;
		private int _currentSegmentIndex;
		private Animation _currentAnimation;

		public KeyframeAnimation(T view, List<(double progress, Action<T> action)> keyframes, double totalDuration, Easing easing = null)
		{
			_view = view;
			_totalDuration = totalDuration;
			_segmentEasing = easing ?? Easing.Linear;
			_sortedKeyframes = keyframes.OrderBy(k => k.progress).ToList();
			Duration = totalDuration;

			// Apply the first keyframe state immediately
			if (_sortedKeyframes.Count > 0 && _sortedKeyframes[0].progress == 0.0)
			{
				_sortedKeyframes[0].action(_view);
			}
		}

		protected override void OnTick(double secondsSinceLastUpdate)
		{
			if (_sortedKeyframes.Count < 2)
			{
				HasFinished = true;
				return;
			}

			if (_currentSegmentIndex >= _sortedKeyframes.Count - 1)
			{
				HasFinished = true;
				return;
			}

			if (_currentAnimation is null)
			{
				_currentAnimation = CreateSegmentAnimation(_currentSegmentIndex);
				if (_currentAnimation is null)
				{
					_currentSegmentIndex++;
					return;
				}
			}

			_currentAnimation.Tick(secondsSinceLastUpdate);

			if (_currentAnimation.HasFinished)
			{
				_currentAnimation = null;
				_currentSegmentIndex++;
			}
		}

		private Animation CreateSegmentAnimation(int index)
		{
			if (index + 1 >= _sortedKeyframes.Count)
				return null;

			var toKeyframe = _sortedKeyframes[index + 1];
			var fromProgress = _sortedKeyframes[index].progress;
			var segmentDuration = (toKeyframe.progress - fromProgress) * _totalDuration;

			return AnimationExtensions.CreateAnimation(
				_view,
				_segmentEasing,
				toKeyframe.action,
				duration: segmentDuration);
		}
	}
}
