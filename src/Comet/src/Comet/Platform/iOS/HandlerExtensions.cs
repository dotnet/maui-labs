using Comet.iOS;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Comet;

public static partial class HandlerExtensions
{
	public static void AddGesture(this IViewHandler handler, Gesture gesture)
	{
		var nativeView = (UIView)handler.PlatformView;
		nativeView.UserInteractionEnabled = true;
		var view = handler.VirtualView as View;

		if (gesture is DragGesture dragGesture)
		{
			var del = new CUIDragInteractionDelegate(dragGesture, view);
			var interaction = new UIDragInteraction(del);
			interaction.Enabled = true;
			nativeView.AddInteraction(interaction);
			gesture.PlatformGesture = interaction;
			return;
		}

		if (gesture is DropGesture dropGesture)
		{
			var del = new CUIDropInteractionDelegate(dropGesture, view);
			var interaction = new UIDropInteraction(del);
			nativeView.AddInteraction(interaction);
			gesture.PlatformGesture = interaction;
			return;
		}

		if (gesture is PointerGesture pointerGesture)
		{
			var hover = new CUIHoverGesture(pointerGesture, view);
			nativeView.AddGestureRecognizer(hover);
			return;
		}

		nativeView.AddGestureRecognizer(gesture.ToGestureRecognizer());
	}

	public static void RemoveGesture(this IViewHandler handler, Gesture gesture)
	{
		var nativeView = (UIView)handler.PlatformView;
		if (gesture.PlatformGesture is UIGestureRecognizer g)
			nativeView.RemoveGestureRecognizer(g);
		else if (gesture.PlatformGesture is IUIInteraction interaction)
			nativeView.RemoveInteraction(interaction);
	}
}


