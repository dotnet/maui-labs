using System;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui;

namespace Comet
{
	/// <summary>
	/// A horizontally-scrolling collection view for displaying items in a carousel pattern.
	/// Extends CollectionView with position tracking, peek, loop, and swipe behavior.
	/// </summary>
	public class CarouselView<T> : CollectionView<T>
	{
		public CarouselView() : base()
		{
			ItemsLayout = ItemsLayout.Horizontal();
		}

		public CarouselView(IReadOnlyList<T> items) : base(new PropertySubscription<IReadOnlyList<T>>(items))
		{
			ItemsLayout = ItemsLayout.Horizontal();
		}

		public CarouselView(Func<IReadOnlyList<T>> items) : base(items)
		{
			ItemsLayout = ItemsLayout.Horizontal();
		}

		PropertySubscription<int> _position;
		public PropertySubscription<int> Position
		{
			get => _position;
			set => this.SetPropertySubscription(ref _position, value);
		}

		PropertySubscription<T> _currentItem;
		public PropertySubscription<T> CurrentItem
		{
			get => _currentItem;
			set => this.SetPropertySubscription(ref _currentItem, value);
		}

		public bool IsBounceEnabled { get; set; } = true;
		public bool IsSwipeEnabled { get; set; } = true;
		public bool IsScrollAnimated { get; set; } = true;
		public bool Loop { get; set; }
		public double PeekAreaInsets { get; set; }

		public Action<int> PositionChanged { get; set; }
		public Action<T> CurrentItemChanged { get; set; }

		public void ScrollTo(int position, bool animate = true)
		{
			ScrollToRequested?.Invoke(position, animate);
		}
	}

	/// <summary>
	/// Non-generic CarouselView for simple scenarios.
	/// </summary>
	public class CarouselView : CollectionView
	{
		PropertySubscription<int> _position;
		public PropertySubscription<int> Position
		{
			get => _position;
			set => this.SetPropertySubscription(ref _position, value);
		}

		public bool IsBounceEnabled { get; set; } = true;
		public bool IsSwipeEnabled { get; set; } = true;
		public bool IsScrollAnimated { get; set; } = true;
		public bool Loop { get; set; }
		public double PeekAreaInsets { get; set; }
	}
}
