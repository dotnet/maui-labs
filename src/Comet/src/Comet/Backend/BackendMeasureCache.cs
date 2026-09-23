#nullable enable
using System;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	/// <summary>
	/// Small per-node cache for intrinsic native measurements. Layouts commonly ask the same
	/// leaf for its size several times while resolving Auto/Grid tracks and arranging the result.
	/// Retained nodes keep those answers across route activation until a mutation invalidates them.
	/// </summary>
	internal sealed class BackendMeasureCache
	{
		const int Capacity = 4;
		readonly Entry[] _entries = new Entry[Capacity];
		int _count;
		int _next;

		public Size GetOrMeasure(
			double widthConstraint,
			double heightConstraint,
			Func<double, double, Size> measure)
		{
			for (var index = 0; index < _count; index++)
			{
				ref readonly var entry = ref _entries[index];
				if (entry.Width.Equals(widthConstraint) &&
					entry.Height.Equals(heightConstraint))
					return entry.Size;
			}

			var size = measure(widthConstraint, heightConstraint);
			_entries[_next] = new Entry(widthConstraint, heightConstraint, size);
			if (_count < Capacity)
				_count++;
			_next = (_next + 1) % Capacity;
			return size;
		}

		public void Clear()
		{
			_count = 0;
			_next = 0;
		}

		readonly record struct Entry(double Width, double Height, Size Size);
	}
}
