#nullable enable
using System;

namespace Comet.Backend;

/// <summary>
/// One-shot initial-list-scroll state shared by retained native list backends.
/// A zero-sized viewport does not consume the request, while ordinary logical
/// owner replacement and row reloads preserve the native list's scroll state.
/// </summary>
internal sealed class ListInitialScrollState : IDisposable
{
	bool _scheduled;
	int _generation;

	public bool IsScheduled => _scheduled;

	public bool TrySchedule(
		double viewportExtent,
		int itemCount,
		int requestedIndex,
		ListScrollPosition position,
		double targetExtent,
		out ListInitialScrollPlan plan)
	{
		plan = default;
		if (_scheduled ||
			!double.IsFinite(viewportExtent) ||
			viewportExtent <= 0 ||
			itemCount <= 0 ||
			requestedIndex < 0)
			return false;

		var index = Math.Clamp(requestedIndex, 0, itemCount - 1);
		var centered = position == ListScrollPosition.Center;
		plan = new ListInitialScrollPlan(
			index,
			position,
			centered ? viewportExtent / 2 : 0,
			centered && double.IsFinite(targetExtent) && targetExtent > 0
				? targetExtent / 2
				: 0,
			_generation);
		_scheduled = true;
		return true;
	}

	public bool IsCurrent(int generation) => generation == _generation;

	public void Invalidate()
	{
		_scheduled = false;
		_generation++;
	}

	public void CancelPending()
	{
		_generation++;
	}

	public static double EdgeSpacing(
		double viewportExtent,
		ListScrollPosition position) =>
		position == ListScrollPosition.Center &&
		double.IsFinite(viewportExtent) &&
		viewportExtent > 0
			? viewportExtent / 2
			: 0;

	public void Dispose() => Invalidate();
}

internal readonly record struct ListInitialScrollPlan(
	int Index,
	ListScrollPosition Position,
	double EdgeSpacing,
	double TargetOffset,
	int Generation);
