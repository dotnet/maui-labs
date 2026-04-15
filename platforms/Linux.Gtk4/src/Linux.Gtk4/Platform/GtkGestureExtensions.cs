using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Gesture recognizer support for GTK4.
/// Maps MAUI gesture recognizers to GTK4 gesture controllers.
/// </summary>
public static class GtkGestureExtensions
{
// Track attached controllers per widget to enable cleanup
private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, List<Gtk.EventController>> _attachedControllers = new();

/// <summary>
/// Attaches MAUI gesture recognizers to a GTK widget.
/// Removes any previously attached gesture controllers first to prevent accumulation.
/// </summary>
public static void AttachGestures(Gtk.Widget widget, IView view)
{
if (view is not Microsoft.Maui.Controls.View mauiView)
return;

RemoveAttachedGestures(widget);

var controllers = new List<Gtk.EventController>();

foreach (var recognizer in mauiView.GestureRecognizers)
{
switch (recognizer)
{
case Microsoft.Maui.Controls.TapGestureRecognizer tapRecognizer:
controllers.AddRange(AttachTapGesture(widget, tapRecognizer));
break;
case Microsoft.Maui.Controls.PanGestureRecognizer panRecognizer:
controllers.AddRange(AttachPanGesture(widget, panRecognizer));
break;
case Microsoft.Maui.Controls.PointerGestureRecognizer pointerRecognizer:
controllers.AddRange(AttachPointerGesture(widget, pointerRecognizer));
break;
case Microsoft.Maui.Controls.SwipeGestureRecognizer swipeRecognizer:
controllers.AddRange(AttachSwipeGesture(widget, swipeRecognizer));
break;
case Microsoft.Maui.Controls.PinchGestureRecognizer pinchRecognizer:
controllers.AddRange(AttachPinchGesture(widget, pinchRecognizer));
break;
}
}

if (controllers.Count > 0)
_attachedControllers[widget.Handle.DangerousGetHandle()] = controllers;
}

/// <summary>
/// Removes all gesture controllers previously attached by <see cref="AttachGestures"/>.
/// </summary>
public static void RemoveAttachedGestures(Gtk.Widget widget)
{
if (_attachedControllers.TryRemove(widget.Handle.DangerousGetHandle(), out var controllers))
{
foreach (var c in controllers)
widget.RemoveController(c);
}
}

private static Gtk.EventController[] AttachTapGesture(Gtk.Widget widget, Microsoft.Maui.Controls.TapGestureRecognizer recognizer)
{
var gesture = Gtk.GestureClick.New();
gesture.OnReleased += (_, args) =>
{
recognizer.Command?.Execute(recognizer.CommandParameter);
};
widget.AddController(gesture);
return [gesture];
}

private static Gtk.EventController[] AttachPanGesture(Gtk.Widget widget, Microsoft.Maui.Controls.PanGestureRecognizer recognizer)
{
var gesture = Gtk.GestureDrag.New();
int gestureId = 0;

gesture.OnDragBegin += (_, args) =>
{
gestureId++;
InvokePanUpdated(recognizer, GestureStatus.Started, gestureId, 0, 0);
};
gesture.OnDragUpdate += (_, args) =>
{
InvokePanUpdated(recognizer, GestureStatus.Running, gestureId, args.OffsetX, args.OffsetY);
};
gesture.OnDragEnd += (_, args) =>
{
InvokePanUpdated(recognizer, GestureStatus.Completed, gestureId, 0, 0);
};
widget.AddController(gesture);
return [gesture];
}

/// <summary>
/// Raises PanUpdated event on a PanGestureRecognizer via reflection,
/// since the event can only be invoked from within the declaring class.
/// </summary>
private static void InvokePanUpdated(
Microsoft.Maui.Controls.PanGestureRecognizer recognizer,
GestureStatus status, int gestureId, double totalX, double totalY)
{
var method = typeof(Microsoft.Maui.Controls.PanGestureRecognizer)
.GetMethod("SendPan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (method != null)
{
try { method.Invoke(recognizer, [status, gestureId, totalX, totalY]); }
catch { /* ignore reflection failures */ }
return;
}

// Fallback: try SendPanUpdated
var method2 = typeof(Microsoft.Maui.Controls.PanGestureRecognizer)
.GetMethod("SendPanUpdated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (method2 != null)
{
try
{
var eventArgs = new Microsoft.Maui.Controls.PanUpdatedEventArgs(status, gestureId, totalX, totalY);
method2.Invoke(recognizer, [eventArgs]);
}
catch { /* ignore reflection failures */ }
}
}

private static Gtk.EventController[] AttachPointerGesture(Gtk.Widget widget, Microsoft.Maui.Controls.PointerGestureRecognizer recognizer)
{
var motion = Gtk.EventControllerMotion.New();

motion.OnEnter += (_, args) =>
{
recognizer.PointerEnteredCommand?.Execute(recognizer.PointerEnteredCommandParameter);
};
motion.OnMotion += (_, args) =>
{
recognizer.PointerMovedCommand?.Execute(recognizer.PointerMovedCommandParameter);
};
motion.OnLeave += (_, _) =>
{
recognizer.PointerExitedCommand?.Execute(recognizer.PointerExitedCommandParameter);
};

// Pointer press/release
var click = Gtk.GestureClick.New();
click.OnPressed += (_, args) =>
{
recognizer.PointerPressedCommand?.Execute(recognizer.PointerPressedCommandParameter);
};
click.OnReleased += (_, args) =>
{
recognizer.PointerReleasedCommand?.Execute(recognizer.PointerReleasedCommandParameter);
};

widget.AddController(motion);
widget.AddController(click);
return [motion, click];
}

private static Gtk.EventController[] AttachSwipeGesture(Gtk.Widget widget, Microsoft.Maui.Controls.SwipeGestureRecognizer recognizer)
{
var gesture = Gtk.GestureSwipe.New();
gesture.OnSwipe += (_, args) =>
{
var mauiView = recognizer.Parent as Microsoft.Maui.Controls.View;
if (mauiView == null) return;

double vx = args.VelocityX;
double vy = args.VelocityY;
SwipeDirection direction;

if (Math.Abs(vx) > Math.Abs(vy))
direction = vx > 0 ? SwipeDirection.Right : SwipeDirection.Left;
else
direction = vy > 0 ? SwipeDirection.Down : SwipeDirection.Up;

if (recognizer.Direction.HasFlag(direction))
recognizer.SendSwiped(mauiView, direction);
};
widget.AddController(gesture);
return [gesture];
}

private static Gtk.EventController[] AttachPinchGesture(Gtk.Widget widget, Microsoft.Maui.Controls.PinchGestureRecognizer recognizer)
{
var gesture = Gtk.GestureZoom.New();
var controller = (Microsoft.Maui.Controls.IPinchGestureController)recognizer;
double startScale = 1.0;
var sender = recognizer.Parent as Microsoft.Maui.Controls.Element ?? recognizer;

gesture.OnBegin += (_, _) =>
{
startScale = gesture.GetScaleDelta();
if (startScale <= 0) startScale = 1.0;
controller.SendPinchStarted(sender, new Point(0, 0));
};
gesture.OnScaleChanged += (_, args) =>
{
controller.SendPinch(sender, args.Scale / startScale, new Point(0, 0));
};
gesture.OnEnd += (_, _) =>
{
controller.SendPinchEnded(sender);
};
gesture.OnCancel += (_, _) =>
{
controller.SendPinchCanceled(sender);
};

widget.AddController(gesture);
return [gesture];
}
}
