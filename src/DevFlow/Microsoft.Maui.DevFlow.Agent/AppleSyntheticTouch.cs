#if IOS || MACCATALYST
using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Microsoft.Maui.DevFlow.Agent;

/// <summary>
/// A synthesised <see cref="UITouch"/>, for the views that recogniser driving cannot reach:
/// MAUI's GraphicsView, and any other UIView that takes input by overriding <c>touchesBegan:</c>
/// instead of installing a gesture recogniser. Those views own no recogniser to drive and no
/// scroll offset to nudge, so a synthetic touch is the only way in. Which views qualify is
/// decided by <see cref="AppleRawTouchPolicy"/>.
///
/// UITouch exposes no public way to set a location or a phase, so both are written through the
/// object's ivars. Every write checks the ivar's type encoding first, and the finished touch is
/// verified through the public API before it is handed to anyone — if UIKit's layout is ever not
/// what we assume, <see cref="Create"/> returns null and the gesture reports "no handler" rather
/// than delivering a nonsense touch. Off by default; opt in with
/// <c>AgentOptions.EnableSyntheticTouch</c>.
/// </summary>
internal sealed class AppleSyntheticTouch : IDisposable
{
    [DllImport("/usr/lib/libobjc.dylib")]
    internal static extern IntPtr object_getClass(IntPtr obj);

    [DllImport("/usr/lib/libobjc.dylib")]
    internal static extern IntPtr class_getMethodImplementation(IntPtr cls, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr class_getInstanceVariable(IntPtr cls, string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_respondsToSelector(IntPtr cls, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern nint ivar_getOffset(IntPtr ivar);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr ivar_getTypeEncoding(IntPtr ivar);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendMessage(IntPtr receiver, IntPtr selector, nint argument);

    /// <summary>alloc/init by class name, for the UIKit types with no usable public constructor.</summary>
    internal static IntPtr Allocate(string className)
    {
        var cls = (IntPtr)Class.GetHandle(className);
        if (cls == IntPtr.Zero)
            return IntPtr.Zero;

        var handle = SendMessage(cls, Selector.GetHandle("alloc"));
        return handle == IntPtr.Zero ? IntPtr.Zero : SendMessage(handle, Selector.GetHandle("init"));
    }

    private CGPoint _location;

    private AppleSyntheticTouch(UITouch touch, CGPoint location)
    {
        Touch = touch;
        _location = location;
    }

    internal UITouch Touch { get; }

    /// <summary>
    /// Builds a touch that reports <paramref name="locationInWindow"/> and starts in the
    /// Began phase, or null when the touch cannot be assembled convincingly.
    /// </summary>
    internal static AppleSyntheticTouch? Create(UIWindow window, UIView view, CGPoint locationInWindow)
    {
        var handle = Allocate("UITouch");
        if (handle == IntPtr.Zero)
            return null;

        // owns: true — the +1 from alloc becomes the wrapper's, so disposing deallocates.
        var touch = Runtime.GetNSObject<UITouch>(handle, owns: true);
        if (touch == null)
            return null;

        var synthetic = new AppleSyntheticTouch(touch, locationInWindow);
        if (synthetic.Configure(window, view, locationInWindow))
            return synthetic;

        synthetic.Dispose();
        return null;
    }

    private bool Configure(UIWindow window, UIView view, CGPoint location)
    {
        var handle = (IntPtr)Touch.Handle;

        // These are strong ivars — the touch releases them when it deallocates, so they are
        // retained on the way in to keep the counts balanced.
        if (!TryWriteObject(handle, "_window", (IntPtr)window.Handle)
            || !TryBindView(handle, view)
            || !TryWritePoint(handle, "_locationInWindow", location)
            || !TrySetPhase(UITouchPhase.Began))
            return false;

        // Best-effort extras: a handler that reads them gets sensible values, and a
        // handler that does not is unaffected if the ivar has moved.
        TryWritePoint(handle, "_previousLocationInWindow", location);
        TryWriteInteger(handle, "_tapCount", 1);
        TryWriteDouble(handle, "_timestamp", NSProcessInfo.ProcessInfo.SystemUptime);

        return Verify(window, location, UITouchPhase.Began);
    }

    /// <summary>
    /// Points the touch at the view that should receive it. UIKit has held this under several
    /// names — a plain <c>_view</c> on older releases, a <c>_responder</c> plus a cached view on
    /// current ones — so each is attempted and one hit is enough.
    /// </summary>
    private static bool TryBindView(IntPtr handle, UIView view)
    {
        var target = (IntPtr)view.Handle;
        var bound = TryWriteObject(handle, "_view", target);
        bound |= TryWriteObject(handle, "_responder", target);
        bound |= TryWriteObject(handle, "_cachedResponderView", target);
        return bound;
    }

    /// <summary>Reads the touch back through the public API to confirm the ivar writes landed.</summary>
    private bool Verify(UIView reference, CGPoint expected, UITouchPhase phase)
    {
        try
        {
            var actual = Touch.LocationInView(reference);
            return Math.Abs(actual.X - expected.X) < 1
                && Math.Abs(actual.Y - expected.Y) < 1
                && Touch.Phase == phase;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Synthetic touch verification failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    internal bool MoveTo(CGPoint locationInWindow)
    {
        var handle = (IntPtr)Touch.Handle;
        TryWritePoint(handle, "_previousLocationInWindow", _location);
        if (!TryWritePoint(handle, "_locationInWindow", locationInWindow))
            return false;

        TryWriteDouble(handle, "_timestamp", NSProcessInfo.ProcessInfo.SystemUptime);
        _location = locationInWindow;
        return TrySetPhase(UITouchPhase.Moved);
    }

    internal bool End() => TrySetPhase(UITouchPhase.Ended);

    private bool TrySetPhase(UITouchPhase phase)
    {
        var handle = (IntPtr)Touch.Handle;
        if (TryWriteInteger(handle, "_phase", (long)phase))
            return true;

        // Some runtimes keep the phase behind a private setter rather than a plain ivar.
        var selector = (IntPtr)Selector.GetHandle("setPhase:");
        if (class_respondsToSelector(object_getClass(handle), selector))
        {
            SendMessage(handle, selector, (nint)(long)phase);
            return true;
        }

        return false;
    }

    private static bool TryGetIvar(IntPtr handle, string name, out int offset, out string encoding)
    {
        offset = 0;
        encoding = string.Empty;

        var ivar = class_getInstanceVariable(object_getClass(handle), name);
        if (ivar == IntPtr.Zero)
            return false;

        offset = (int)ivar_getOffset(ivar);
        encoding = Marshal.PtrToStringAnsi(ivar_getTypeEncoding(ivar)) ?? string.Empty;
        return encoding.Length > 0;
    }

    private static bool TryWriteObject(IntPtr handle, string name, IntPtr value)
    {
        if (!TryGetIvar(handle, name, out var offset, out var encoding) || encoding[0] != '@')
            return false;

        if (value != IntPtr.Zero)
            SendMessage(value, Selector.GetHandle("retain"));

        Marshal.WriteIntPtr(handle, offset, value);
        return true;
    }

    private static bool TryWriteInteger(IntPtr handle, string name, long value)
    {
        if (!TryGetIvar(handle, name, out var offset, out var encoding))
            return false;

        switch (encoding[0])
        {
            case 'c' or 'C' or 'B':
                Marshal.WriteByte(handle, offset, (byte)value);
                return true;
            case 's' or 'S':
                Marshal.WriteInt16(handle, offset, (short)value);
                return true;
            case 'i' or 'I' or 'l' or 'L':
                Marshal.WriteInt32(handle, offset, (int)value);
                return true;
            case 'q' or 'Q':
                Marshal.WriteInt64(handle, offset, value);
                return true;
            default:
                return false;
        }
    }

    private static bool TryWriteDouble(IntPtr handle, string name, double value)
    {
        if (!TryGetIvar(handle, name, out var offset, out var encoding))
            return false;

        switch (encoding[0])
        {
            case 'd':
                Marshal.WriteInt64(handle, offset, BitConverter.DoubleToInt64Bits(value));
                return true;
            case 'f':
                Marshal.WriteInt32(handle, offset, BitConverter.SingleToInt32Bits((float)value));
                return true;
            default:
                return false;
        }
    }

    private static bool TryWritePoint(IntPtr handle, string name, CGPoint value)
    {
        if (!TryGetIvar(handle, name, out var offset, out var encoding)
            || encoding[0] != '{'
            || !encoding.Contains("CGPoint", StringComparison.Ordinal))
            return false;

        Marshal.StructureToPtr(value, IntPtr.Add(handle, offset), false);
        return true;
    }

    public void Dispose() => Touch.Dispose();
}

/// <summary>
/// A UIEvent that reports the synthesised touches as its own. Subclassing is what keeps this
/// tier on public API: UIKit offers no way to put touches into an event, but every consumer
/// reaches them through <c>allTouches</c> or <c>touchesForView:</c>, both of which are virtual.
/// </summary>
internal sealed class SyntheticTouchEvent : UIEvent
{
    private readonly NSSet _touches;

    internal SyntheticTouchEvent(NSSet touches) => _touches = touches;

    public override NSSet? AllTouches => _touches;

    public override NSSet? TouchesForView(UIView view) => _touches;

    public override NSSet? TouchesForWindow(UIWindow window) => _touches;

    public override UIEventType Type => UIEventType.Touches;
}

/// <summary>
/// Drives a whole touch sequence — down, moves, up — into a raw-touch view. The shape mirrors
/// the Android <c>MotionEvent</c> injector: <c>positionsAt</c> is sampled with t in 0..1 and
/// returns one window-coordinate point per finger.
/// </summary>
internal static class AppleTouchInjector
{
    /// <summary>
    /// Delivers the sequence to <paramref name="target"/>, which the caller has already
    /// resolved and accepted. Nothing is delivered unless every finger's starting point
    /// hit-tests to that same view: a real finger landing on an overlay would have gone to
    /// the overlay, so a mismatch is reported as unhandled rather than routed anyway.
    /// </summary>
    internal static async Task<bool> InjectAsync(
        UIView target,
        Func<double, CGPoint[]> positionsAt,
        int durationMs,
        int steps,
        int holdMs = 0)
    {
        var window = target.Window;
        if (window == null)
            return false;

        var start = positionsAt(0);
        if (start.Length == 0)
            return false;

        foreach (var point in start)
        {
            if (!ReferenceEquals(window.HitTest(point, null), target))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Microsoft.Maui.DevFlow] Synthetic touch at ({point.X:0.#}, {point.Y:0.#}) does not land on {target.GetType().Name}.");
                return false;
            }
        }

        var touches = new List<AppleSyntheticTouch>(start.Length);

        try
        {
            foreach (var point in start)
            {
                var touch = AppleSyntheticTouch.Create(window, target, point);
                if (touch == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[Microsoft.Maui.DevFlow] Synthetic UITouch could not be assembled on this OS version.");
                    return false;
                }

                touches.Add(touch);
            }

            using var set = new NSSet(touches.Select(t => (NSObject)t.Touch).ToArray());
            // Not every raw-touch view reads the touch set it is handed: MAUI's GraphicsView
            // asks the event for the touches in the view, so the event has to answer honestly.
            using var touchEvent = new SyntheticTouchEvent(set);
            target.TouchesBegan(set, touchEvent);

            if (holdMs > 0)
                await Task.Delay(holdMs);

            var stepDelay = steps > 0 && durationMs > 0 ? Math.Max(1, durationMs / steps) : 0;
            for (var step = 1; step <= steps; step++)
            {
                var points = positionsAt((double)step / steps);
                for (var i = 0; i < touches.Count && i < points.Length; i++)
                    touches[i].MoveTo(points[i]);

                target.TouchesMoved(set, touchEvent);
                if (stepDelay > 0)
                    await Task.Delay(stepDelay);
            }

            foreach (var touch in touches)
                touch.End();
            target.TouchesEnded(set, touchEvent);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Synthetic touch injection failed: {ex.GetBaseException().Message}");
            return false;
        }
        finally
        {
            foreach (var touch in touches)
                touch.Dispose();
        }
    }

}

/// <summary>
/// Decides which views may be handed synthesised touches. Overriding <c>touchesBegan:</c> is
/// not enough of a signal — UIControl, UIScrollView and text views all override it, and a pan
/// delivered to a UIButton fires its action — so acceptance is a positive allow-list: MAUI's
/// GraphicsView, plus any type the app names in <c>AgentOptions.SyntheticTouchViewTypes</c>.
/// The exclusions win over a registration, so naming a base class cannot opt controls back in.
/// </summary>
internal sealed class AppleRawTouchPolicy
{
    private readonly HashSet<string> _registeredTypes;

    internal AppleRawTouchPolicy(IEnumerable<string> registeredTypes)
        => _registeredTypes = new HashSet<string>(
            registeredTypes.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()),
            StringComparer.Ordinal);

    /// <summary>
    /// The accepted view a finger at <paramref name="pointInWindow"/> would land on, provided it is
    /// the element the caller named or part of it. An overlay from some other element — or any
    /// view the policy refuses — resolves to null, and the gesture is reported as unhandled.
    /// </summary>
    internal UIView? Resolve(UIView element, CGPoint pointInWindow)
        => element.Window?.HitTest(pointInWindow, null) is { } hit
           && (ReferenceEquals(hit, element) || hit.IsDescendantOfView(element))
           && Accepts(hit)
            ? hit
            : null;

    internal bool Accepts(UIView view)
        => !IsExcluded(view) && IsRegistered(view) && OverridesTouchesBegan(view);

    private static bool IsExcluded(UIView view)
    {
        // Controls fire actions from their own touch tracking, and scroll views — table,
        // collection and text views among them — scroll from it.
        if (view is UIControl or UIScrollView)
            return true;

        // A view whose input goes through a recognizer never sees touches handed straight to
        // the view. SkiaSharp's SKCanvasView is one: its SKTouchHandler is a UIGestureRecognizer.
        // Hover is the exception — GraphicsView installs one, and it takes no touches.
        return view.GestureRecognizers?.Any(r => r.Enabled && r is not UIHoverGestureRecognizer) == true;
    }

    private bool IsRegistered(UIView view)
    {
        if (view is Microsoft.Maui.Platform.PlatformTouchGraphicsView)
            return true;

        // Stops short of UIView itself: registering the root type would accept everything.
        for (var type = view.GetType(); type != null && type != typeof(UIView); type = type.BaseType)
        {
            if (_registeredTypes.Contains(type.Name)
                || (type.FullName is { } fullName && _registeredTypes.Contains(fullName)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A registered view that inherits UIView's <c>touchesBegan:withEvent:</c> would only forward
    /// the touches up the responder chain, to whatever happens to sit above it.
    /// </summary>
    private static bool OverridesTouchesBegan(UIView view)
    {
        try
        {
            var selector = (IntPtr)Selector.GetHandle("touchesBegan:withEvent:");
            var stock = AppleSyntheticTouch.class_getMethodImplementation(
                (IntPtr)Class.GetHandle("UIView"), selector);
            var actual = AppleSyntheticTouch.class_getMethodImplementation(
                AppleSyntheticTouch.object_getClass((IntPtr)view.Handle), selector);
            return stock != IntPtr.Zero && actual != IntPtr.Zero && actual != stock;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Raw-touch probe failed: {ex.GetBaseException().Message}");
            return false;
        }
    }
}
#endif
