using System.Collections;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// CarouselView handler using Gtk.Stack for one-item-at-a-time display
/// with animated slide transitions. Navigation via swipe gesture,
/// programmatic Position changes, or Prev/Next buttons.
/// </summary>
public class CarouselViewHandler : GtkViewHandler<IView, Gtk.Box>
{
	Gtk.Stack? _stack;
	readonly List<string> _childNames = new();
	readonly List<object> _dataItems = new();
	int _currentPosition;
	bool _isVertical;

	public static new IPropertyMapper<IView, CarouselViewHandler> Mapper =
		new PropertyMapper<IView, CarouselViewHandler>(ViewMapper)
		{
			["ItemsSource"] = MapItemsSource,
			["Position"] = MapPosition,
			["CurrentItem"] = MapCurrentItem,
			["Loop"] = MapLoop,
			["IsBounceEnabled"] = MapIsBounceEnabled,
			["IsSwipeEnabled"] = MapIsSwipeEnabled,
			["PeekAreaInsets"] = MapPeekAreaInsets,
			["ItemTemplate"] = MapItemsSource,
			["ItemsLayout"] = MapItemsLayout,
		};

	public CarouselViewHandler() : base(Mapper) { }

	protected override Gtk.Box CreatePlatformView()
	{
		var outer = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		outer.SetVexpand(true);
		outer.SetHexpand(true);

		_stack = Gtk.Stack.New();
		_stack.SetTransitionType(Gtk.StackTransitionType.SlideLeft);
		_stack.SetTransitionDuration(250);
		_stack.SetVexpand(true);
		_stack.SetHexpand(true);

		outer.Append(_stack);
		return outer;
	}

	protected override void ConnectHandler(Gtk.Box platformView)
	{
		base.ConnectHandler(platformView);

		// Add swipe gesture for navigation
		var swipe = Gtk.GestureSwipe.New();
		swipe.OnSwipe += OnSwipe;
		platformView.AddController(swipe);

		if (VirtualView is CarouselView cv)
			PopulateItems(cv);
	}

	void OnSwipe(Gtk.GestureSwipe sender, Gtk.GestureSwipe.SwipeSignalArgs args)
	{
		if (_childNames.Count == 0 || VirtualView is not CarouselView cv) return;

		int newPos;
		if (_isVertical)
			newPos = args.VelocityY < 0 ? _currentPosition + 1 : _currentPosition - 1;
		else
			newPos = args.VelocityX < 0 ? _currentPosition + 1 : _currentPosition - 1;

		if (cv.Loop)
			newPos = ((newPos % _childNames.Count) + _childNames.Count) % _childNames.Count;
		else
			newPos = Math.Clamp(newPos, 0, _childNames.Count - 1);

		if (newPos != _currentPosition)
		{
			// Set transition direction based on navigation
			if (_stack != null)
			{
				bool forward = newPos > _currentPosition;
				_stack.SetTransitionType(_isVertical
					? (forward ? Gtk.StackTransitionType.SlideUp : Gtk.StackTransitionType.SlideDown)
					: (forward ? Gtk.StackTransitionType.SlideLeft : Gtk.StackTransitionType.SlideRight));
			}
			_currentPosition = newPos;
			cv.Position = newPos;
			ShowCurrentPage();
		}
	}

	public static void MapItemsSource(CarouselViewHandler handler, IView view)
	{
		if (view is not CarouselView cv) return;
		handler.PopulateItems(cv);
	}

	public static void MapPosition(CarouselViewHandler handler, IView view)
	{
		if (view is not CarouselView cv) return;
		int newPos = cv.Position;
		if (newPos != handler._currentPosition && handler._stack != null)
		{
			bool forward = newPos > handler._currentPosition;
			handler._stack.SetTransitionType(handler._isVertical
				? (forward ? Gtk.StackTransitionType.SlideUp : Gtk.StackTransitionType.SlideDown)
				: (forward ? Gtk.StackTransitionType.SlideLeft : Gtk.StackTransitionType.SlideRight));
		}
		handler._currentPosition = newPos;
		handler.ShowCurrentPage();
	}

	public static void MapCurrentItem(CarouselViewHandler handler, IView view) { }

	public static void MapItemsLayout(CarouselViewHandler handler, IView view)
	{
		if (view is not CarouselView cv) return;
		handler._isVertical = cv.ItemsLayout is LinearItemsLayout l && l.Orientation == ItemsLayoutOrientation.Vertical;
	}

	public static void MapPeekAreaInsets(CarouselViewHandler handler, IView view)
	{
		if (view is not CarouselView cv || handler._stack == null) return;
		var insets = cv.PeekAreaInsets;
		handler._stack.SetMarginStart((int)insets.Left);
		handler._stack.SetMarginEnd((int)insets.Right);
		handler._stack.SetMarginTop((int)insets.Top);
		handler._stack.SetMarginBottom((int)insets.Bottom);
	}

	public static void MapLoop(CarouselViewHandler handler, IView view) { }
	public static void MapIsBounceEnabled(CarouselViewHandler handler, IView view) { }
	public static void MapIsSwipeEnabled(CarouselViewHandler handler, IView view) { }

	void PopulateItems(CarouselView cv)
	{
		if (_stack == null) return;

		// Clear existing
		_childNames.Clear();
		_dataItems.Clear();
		while (_stack.GetFirstChild() is Gtk.Widget child)
			_stack.Remove(child);

		if (cv.ItemsSource is not IEnumerable items) return;

		int idx = 0;
		foreach (var item in items)
		{
			var name = $"page_{idx}";
			_childNames.Add(name);
			_dataItems.Add(item!);

			var card = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
			card.SetVexpand(true);
			card.SetHexpand(true);
			card.SetHalign(Gtk.Align.Fill);
			card.SetValign(Gtk.Align.Center);

			var label = Gtk.Label.New(item?.ToString() ?? "");
			label.SetWrap(true);
			label.SetHalign(Gtk.Align.Center);
			label.SetValign(Gtk.Align.Center);
			label.SetHexpand(true);
			card.Append(label);

			_stack.AddNamed(card, name);
			idx++;
		}

		_currentPosition = Math.Clamp(cv.Position, 0, Math.Max(0, _childNames.Count - 1));
		ShowCurrentPage();
	}

	void ShowCurrentPage()
	{
		if (_stack == null || _childNames.Count == 0) return;
		int pos = Math.Clamp(_currentPosition, 0, _childNames.Count - 1);
		_stack.SetVisibleChildName(_childNames[pos]);
	}
}
