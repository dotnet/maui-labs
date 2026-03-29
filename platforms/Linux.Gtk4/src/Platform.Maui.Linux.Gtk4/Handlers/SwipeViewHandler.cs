using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Handler for SwipeView with swipe-to-reveal action buttons.
/// Uses Gtk.Fixed for manual positioning: content slides left/right to reveal
/// action buttons behind it. Gtk.GestureDrag drives the swipe.
/// </summary>
public class SwipeViewHandler : GtkViewHandler<IView, Gtk.Box>
{
	Gtk.Fixed? _fixed;
	Gtk.Box? _contentBox;
	Gtk.Box? _leftActions;
	Gtk.Box? _rightActions;
	double _dragOffset;
	double _committedOffset;
	int _contentWidth;
	bool _isOpen;

	public static new IPropertyMapper<IView, SwipeViewHandler> Mapper =
		new PropertyMapper<IView, SwipeViewHandler>(ViewMapper)
		{
			["Content"] = MapContent,
			["LeftItems"] = MapSwipeItems,
			["RightItems"] = MapSwipeItems,
			["TopItems"] = MapSwipeItems,
			["BottomItems"] = MapSwipeItems,
		};

	public SwipeViewHandler() : base(Mapper) { }

	protected override Gtk.Box CreatePlatformView()
	{
		var outer = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		outer.SetHexpand(true);
		outer.SetOverflow(Gtk.Overflow.Hidden);

		_fixed = Gtk.Fixed.New();
		_fixed.SetHexpand(true);
		_fixed.SetOverflow(Gtk.Overflow.Hidden);

		// Action buttons behind content
		_leftActions = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
		_leftActions.SetVisible(false);
		_rightActions = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
		_rightActions.SetVisible(false);

		// Content sits on top, slides left/right
		_contentBox = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
		_contentBox.SetHexpand(true);

		_fixed.Put(_leftActions, 0, 0);
		_fixed.Put(_rightActions, 0, 0);
		_fixed.Put(_contentBox, 0, 0);

		outer.Append(_fixed);

		// Swipe gesture
		var drag = Gtk.GestureDrag.New();
		drag.OnDragBegin += OnDragBegin;
		drag.OnDragUpdate += OnDragUpdate;
		drag.OnDragEnd += OnDragEnd;
		outer.AddController(drag);

		return outer;
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		if (_contentBox != null && _fixed != null)
		{
			int w = (int)rect.Width;
			int h = (int)rect.Height;
			_contentBox.SetSizeRequest(w, h);
			_fixed.SetSizeRequest(w, h);
			_leftActions?.SetSizeRequest(-1, h);
			_rightActions?.SetSizeRequest(-1, h);

			// Position right actions at the right edge
			if (_rightActions?.GetVisible() == true)
				_fixed.Move(_rightActions, w - 180, 0);
		}
	}

	void OnDragBegin(Gtk.GestureDrag sender, Gtk.GestureDrag.DragBeginSignalArgs args)
	{
		_dragOffset = _committedOffset;
		_contentWidth = _contentBox?.GetAllocatedWidth() ?? 400;
	}

	void OnDragUpdate(Gtk.GestureDrag sender, Gtk.GestureDrag.DragUpdateSignalArgs args)
	{
		if (_fixed == null || _contentBox == null) return;

		double newOffset = _committedOffset + args.OffsetX;

		// Clamp: only allow left swipe if right items exist, right swipe if left items exist
		bool hasLeft = _leftActions?.GetFirstChild() != null;
		bool hasRight = _rightActions?.GetFirstChild() != null;

		int maxReveal = 180;
		if (!hasRight) newOffset = Math.Max(0, newOffset);
		if (!hasLeft) newOffset = Math.Min(0, newOffset);
		newOffset = Math.Clamp(newOffset, -maxReveal, maxReveal);

		// Show only the side being revealed, hide the other
		_leftActions?.SetVisible(hasLeft && newOffset > 0);
		_rightActions?.SetVisible(hasRight && newOffset < 0);

		_dragOffset = newOffset;
		_fixed.Move(_contentBox, newOffset, 0);
	}

	void OnDragEnd(Gtk.GestureDrag sender, Gtk.GestureDrag.DragEndSignalArgs args)
	{
		if (_fixed == null || _contentBox == null) return;

		const double threshold = 60;

		if (Math.Abs(_dragOffset) > threshold)
		{
			// Commit — snap open
			int snapTo = _dragOffset > 0 ? 180 : -180;
			_committedOffset = snapTo;
			_isOpen = true;
			_fixed.Move(_contentBox, snapTo, 0);
		}
		else
		{
			// Cancel — snap closed, hide action buttons
			_committedOffset = 0;
			_isOpen = false;
			_fixed.Move(_contentBox, 0, 0);
			_leftActions?.SetVisible(false);
			_rightActions?.SetVisible(false);
		}
	}

	public static void MapContent(SwipeViewHandler handler, IView view)
	{
		if (view is not SwipeView swipeView || handler.MauiContext == null || handler._contentBox == null)
			return;

		// Clear existing
		while (handler._contentBox.GetFirstChild() is Gtk.Widget child)
			handler._contentBox.Remove(child);

		if (swipeView.Content != null)
		{
			var platformContent = (Gtk.Widget)swipeView.Content.ToPlatform(handler.MauiContext);
			platformContent.SetVexpand(true);
			platformContent.SetHexpand(true);
			handler._contentBox.Append(platformContent);
		}

		// Rebuild action buttons whenever content is mapped
		BuildActionButtons(handler, swipeView);
	}

	public static void MapSwipeItems(SwipeViewHandler handler, IView view)
	{
		if (view is not SwipeView swipeView) return;
		BuildActionButtons(handler, swipeView);
	}

	static void BuildActionButtons(SwipeViewHandler handler, SwipeView swipeView)
	{
		BuildSideActions(handler._leftActions, swipeView.LeftItems, handler);
		BuildSideActions(handler._rightActions, swipeView.RightItems, handler);
	}

	static void BuildSideActions(Gtk.Box? container, SwipeItems? items, SwipeViewHandler handler)
	{
		if (container == null) return;

		while (container.GetFirstChild() is Gtk.Widget child)
			container.Remove(child);

		if (items == null || items.Count == 0)
		{
			container.SetVisible(false);
			return;
		}

		// Keep hidden until user starts swiping
		container.SetVisible(false);

		foreach (SwipeItem item in items)
		{
			var btn = Gtk.Button.New();
			var btnBox = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
			btnBox.SetHalign(Gtk.Align.Center);
			btnBox.SetValign(Gtk.Align.Center);

			if (!string.IsNullOrEmpty(item.Text))
			{
				var label = Gtk.Label.New(item.Text);
				label.SetHalign(Gtk.Align.Center);
				btnBox.Append(label);
			}

			btn.SetChild(btnBox);
			btn.SetSizeRequest(90, -1);
			btn.SetVexpand(true);

			// Style with background color
			var bg = item.BackgroundColor ?? Colors.LightGray;
			var cssProvider = Gtk.CssProvider.New();
			cssProvider.LoadFromString(
				$"button {{ background-image: none; background-color: rgba({(int)(bg.Red*255)},{(int)(bg.Green*255)},{(int)(bg.Blue*255)},{bg.Alpha}); color: white; border-radius: 0; border: none; }}");
			btn.GetStyleContext().AddProvider(cssProvider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);

			var capturedItem = item;
			btn.OnClicked += (s, e) =>
			{
				capturedItem.Command?.Execute(capturedItem.CommandParameter);
				((Microsoft.Maui.ISwipeItem)capturedItem).OnInvoked();
				// Close after action
				handler._committedOffset = 0;
				handler._isOpen = false;
				handler._fixed?.Move(handler._contentBox!, 0, 0);
				handler._leftActions?.SetVisible(false);
				handler._rightActions?.SetVisible(false);
			};

			container.Append(btn);
		}
	}
}
