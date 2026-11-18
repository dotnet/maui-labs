using Microsoft.Maui.Graphics;
using Microsoft.Maui.DevFlow.Agent.Core;
#if ANDROID
using Android.OS;
using Android.Views;
#endif
#if IOS || MACCATALYST
using CoreGraphics;
using UIKit;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

public partial class PlatformAgentService
{
    protected override PointerStreamSupport GetPointerStreamSupport()
    {
#if ANDROID
        return new PointerStreamSupport(
            Supported: true,
            Fidelity: "native",
            RequiresOptIn: false,
            Reason: null);
#elif IOS || MACCATALYST
        return new PointerStreamSupport(
            Supported: _options.EnableSyntheticTouch,
            Fidelity: "synthetic",
            RequiresOptIn: true,
            Reason: _options.EnableSyntheticTouch
                ? null
                : "Pointer streaming on iOS and Mac Catalyst requires AgentOptions.EnableSyntheticTouch.");
#else
        return PointerStreamSupport.Unsupported(
            $"Continuous pointer injection is not supported on {DeviceInfo.Platform}.");
#endif
    }

    protected override Task<PointerSessionBackendStartResult> StartPointerSessionBackendAsync(
        PointerSessionStartRequest request,
        CancellationToken cancellationToken)
    {
#if ANDROID
        return DispatchPointerStartAsync(
            () => StartAndroidPointerSession(request),
            cancellationToken);
#elif IOS || MACCATALYST
        return DispatchPointerStartAsync(
            () => StartApplePointerSession(request),
            cancellationToken);
#else
        return base.StartPointerSessionBackendAsync(request, cancellationToken);
#endif
    }

    protected override Task<PointerSessionBackendResult> MovePointerSessionBackendAsync(
        object state,
        IReadOnlyList<PointerSessionSample> samples,
        CancellationToken cancellationToken)
    {
#if ANDROID
        return DispatchAsync(
            () => MoveAndroidPointerSession(state, samples, cancellationToken),
            cancellationToken);
#elif IOS || MACCATALYST
        return DispatchAsync(
            () => MoveApplePointerSession(state, samples, cancellationToken),
            cancellationToken);
#else
        return base.MovePointerSessionBackendAsync(state, samples, cancellationToken);
#endif
    }

    protected override Task<PointerSessionBackendResult> EndPointerSessionBackendAsync(
        object state,
        PointerSessionSample sample,
        CancellationToken cancellationToken)
    {
#if ANDROID
        return DispatchAsync(
            () => EndAndroidPointerSession(state, sample, cancellationToken),
            cancellationToken);
#elif IOS || MACCATALYST
        return DispatchAsync(
            () => EndApplePointerSession(state, sample, cancellationToken),
            cancellationToken);
#else
        return base.EndPointerSessionBackendAsync(state, sample, cancellationToken);
#endif
    }

    protected override Task<PointerSessionBackendResult> CancelPointerSessionBackendAsync(
        object state,
        string reason)
    {
#if ANDROID
        return DispatchAsync(() => CancelAndroidPointerSession(state, reason));
#elif IOS || MACCATALYST
        return DispatchAsync(() => CancelApplePointerSession(state, reason));
#else
        return base.CancelPointerSessionBackendAsync(state, reason);
#endif
    }

#if ANDROID
    private sealed class AndroidPointerSessionState
    {
        public required global::Android.Views.View Root { get; init; }
        public required float Density { get; init; }
        public required float RootOffsetX { get; init; }
        public required float RootOffsetY { get; init; }
        public required long DownTime { get; init; }
        public long LastEventTime { get; set; }
        public float LastX { get; set; }
        public float LastY { get; set; }
    }

    private PointerSessionBackendStartResult StartAndroidPointerSession(PointerSessionStartRequest request)
    {
        var windowIndex = request.WindowId ?? 0;
        var mauiWindow = _app?.Windows.ElementAtOrDefault(windowIndex);
        if (request.WindowId.HasValue && mauiWindow is null)
        {
            return PointerSessionBackendStartResult.Failed(
                $"Window {request.WindowId.Value} is no longer available.",
                409,
                "pointer-window-unavailable");
        }

        var activity = mauiWindow?.Handler?.PlatformView as global::Android.App.Activity;
        if (request.WindowId.HasValue && activity is null)
        {
            return PointerSessionBackendStartResult.Failed(
                $"Window {request.WindowId.Value} is not backed by an Android activity.",
                409,
                "pointer-window-unavailable");
        }
        activity ??= global::Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (ResolveAndroidPointerRoot(activity, request.X, request.Y) is not { } target)
            return PointerSessionBackendStartResult.Failed("The Android window is not laid out.");

        if (target.BlockedByDialog)
        {
            return PointerSessionBackendStartResult.Failed(
                "The pointer is outside the visible modal dialog; the underlying activity was not touched.",
                422,
                "pointer-blocked-by-dialog");
        }

        var x = (float)(request.X * target.Density) - target.RootOffsetX;
        var y = (float)(request.Y * target.Density) - target.RootOffsetY;
        if (x < 0 || y < 0 || x >= target.Root.Width || y >= target.Root.Height)
        {
            return PointerSessionBackendStartResult.Failed(
                $"Pointer start ({request.X:0.#}, {request.Y:0.#}) is outside the Android window.",
                422,
                "pointer-outside-window");
        }

        var downTime = SystemClock.UptimeMillis();
        using var down = MotionEvent.Obtain(
            downTime,
            downTime,
            MotionEventActions.Down,
            x,
            y,
            0);
        if (!target.Root.DispatchTouchEvent(down))
        {
            using var cancel = MotionEvent.Obtain(
                downTime,
                Math.Max(downTime + 1, SystemClock.UptimeMillis()),
                MotionEventActions.Cancel,
                x,
                y,
                0);
            target.Root.DispatchTouchEvent(cancel);
            return PointerSessionBackendStartResult.Failed(
                $"Nothing at ({request.X:0.#}, {request.Y:0.#}) accepted the Android touch.",
                422,
                "pointer-not-handled");
        }

        return PointerSessionBackendStartResult.Started(
            new AndroidPointerSessionState
            {
                Root = target.Root,
                Density = target.Density,
                RootOffsetX = target.RootOffsetX,
                RootOffsetY = target.RootOffsetY,
                DownTime = downTime,
                LastEventTime = downTime,
                LastX = x,
                LastY = y
            },
            $"MotionEvent pointer down at ({request.X:0.#}, {request.Y:0.#})");
    }

    private static PointerSessionBackendResult MoveAndroidPointerSession(
        object state,
        IReadOnlyList<PointerSessionSample> samples,
        CancellationToken cancellationToken)
    {
        if (state is not AndroidPointerSessionState session)
            return PointerSessionBackendResult.Failed("Invalid Android pointer session state.", 500);

        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eventTime = AndroidPointerEventTime(session, sample.ElapsedMs);
            var x = (float)(sample.X * session.Density) - session.RootOffsetX;
            var y = (float)(sample.Y * session.Density) - session.RootOffsetY;
            using var move = MotionEvent.Obtain(
                session.DownTime,
                eventTime,
                MotionEventActions.Move,
                x,
                y,
                0);
            if (!session.Root.DispatchTouchEvent(move))
                return PointerSessionBackendResult.Failed("The Android view stopped handling the pointer stream.");
            session.LastEventTime = eventTime;
            session.LastX = x;
            session.LastY = y;
        }

        return PointerSessionBackendResult.Applied(
            $"Applied {samples.Count} coalesced MotionEvent sample(s).");
    }

    private static PointerSessionBackendResult EndAndroidPointerSession(
        object state,
        PointerSessionSample sample,
        CancellationToken cancellationToken)
    {
        if (state is not AndroidPointerSessionState session)
            return PointerSessionBackendResult.Failed("Invalid Android pointer session state.", 500);
        cancellationToken.ThrowIfCancellationRequested();

        var eventTime = AndroidPointerEventTime(session, sample.ElapsedMs);
        var x = (float)(sample.X * session.Density) - session.RootOffsetX;
        var y = (float)(sample.Y * session.Density) - session.RootOffsetY;
        if (x != session.LastX || y != session.LastY)
        {
            using var move = MotionEvent.Obtain(
                session.DownTime,
                eventTime,
                MotionEventActions.Move,
                x,
                y,
                0);
            session.Root.DispatchTouchEvent(move);
            eventTime = Math.Max(eventTime + 1, SystemClock.UptimeMillis());
        }

        using var up = MotionEvent.Obtain(
            session.DownTime,
            eventTime,
            MotionEventActions.Up,
            x,
            y,
            0);
        session.Root.DispatchTouchEvent(up);
        return PointerSessionBackendResult.Applied(
            $"MotionEvent pointer up at ({sample.X:0.#}, {sample.Y:0.#})");
    }

    private static PointerSessionBackendResult CancelAndroidPointerSession(object state, string reason)
    {
        if (state is not AndroidPointerSessionState session)
            return PointerSessionBackendResult.Failed("Invalid Android pointer session state.", 500);

        var eventTime = Math.Max(session.LastEventTime + 1, SystemClock.UptimeMillis());
        using var cancel = MotionEvent.Obtain(
            session.DownTime,
            eventTime,
            MotionEventActions.Cancel,
            session.LastX,
            session.LastY,
            0);
        session.Root.DispatchTouchEvent(cancel);
        return PointerSessionBackendResult.Applied($"MotionEvent pointer cancel ({reason}).");
    }

    private static long AndroidPointerEventTime(
        AndroidPointerSessionState session,
        double elapsedMs)
        => Math.Max(
            session.LastEventTime + 1,
            session.DownTime + (long)Math.Round(elapsedMs));

    private readonly record struct AndroidPointerRoot(
        global::Android.Views.View Root,
        float Density,
        float RootOffsetX,
        float RootOffsetY,
        bool BlockedByDialog);

    private AndroidPointerRoot? ResolveAndroidPointerRoot(
        global::Android.App.Activity? activity,
        double windowX,
        double windowY)
    {
        var decor = activity?.Window?.DecorView?.RootView;
        if (decor is null || !decor.IsAttachedToWindow || decor.Width <= 1 || decor.Height <= 1)
            return null;

        var density = decor.Resources?.DisplayMetrics?.Density ?? 1f;
        var decorLocation = new int[2];
        decor.GetLocationOnScreen(decorLocation);
        var screenX = decorLocation[0] + (float)(windowX * density);
        var screenY = decorLocation[1] + (float)(windowY * density);
        var hasVisibleDialog = false;

        foreach (var registration in NativeElementRegistry?.GetSnapshot().Reverse()
            ?? Enumerable.Empty<NativeElementRegistrationSnapshot>())
        {
            var isDialog = registration.Role.Equals("Dialog", StringComparison.Ordinal);
            if (!isDialog && !registration.Role.Equals("ShellTabOverflow", StringComparison.Ordinal))
                continue;
            if (registration.NativeElement is not global::Android.Views.View registeredView
                || !ReferenceEquals(FindAndroidActivity(registeredView.Context), activity))
                continue;

            var root = registeredView.RootView;
            hasVisibleDialog |= isDialog
                && root is { IsAttachedToWindow: true, IsShown: true };
            if (root is null
                || ReferenceEquals(root, decor)
                || !root.IsAttachedToWindow
                || !root.IsShown
                || !root.HasWindowFocus
                || root.Width <= 1
                || root.Height <= 1)
                continue;

            var location = new int[2];
            root.GetLocationOnScreen(location);
            if (screenX >= location[0]
                && screenY >= location[1]
                && screenX < location[0] + root.Width
                && screenY < location[1] + root.Height)
            {
                return new AndroidPointerRoot(
                    root,
                    density,
                    location[0] - decorLocation[0],
                    location[1] - decorLocation[1],
                    BlockedByDialog: false);
            }
        }

        return new AndroidPointerRoot(
            decor,
            density,
            RootOffsetX: 0,
            RootOffsetY: 0,
            BlockedByDialog: hasVisibleDialog || !decor.HasWindowFocus);
    }
#endif

#if IOS || MACCATALYST
    private sealed class ApplePointerSessionState : IDisposable
    {
        public required AppleTouchInjector.PointerSession Session { get; init; }
        public required UIView Target { get; init; }
        public CGPoint LastPoint { get; set; }

        public void Dispose() => Session.Dispose();
    }

    private PointerSessionBackendStartResult StartApplePointerSession(PointerSessionStartRequest request)
    {
        if (!_options.EnableSyntheticTouch)
        {
            return PointerSessionBackendStartResult.Failed(
                "Pointer streaming requires AgentOptions.EnableSyntheticTouch on iOS and Mac Catalyst.",
                501,
                "not_supported");
        }

        var windowIndex = request.WindowId ?? 0;
        if (_app?.Windows.ElementAtOrDefault(windowIndex)?.Handler?.PlatformView is not UIWindow window)
            return PointerSessionBackendStartResult.Failed("The requested UIKit window was not found.");

        var point = new CGPoint(request.X, request.Y);
        if (!window.Bounds.Contains(point))
        {
            return PointerSessionBackendStartResult.Failed(
                $"Pointer start ({request.X:0.#}, {request.Y:0.#}) is outside the UIKit window.",
                422,
                "pointer-outside-window");
        }

        var hit = window.HitTest(point, null);
        if (hit is null)
            return PointerSessionBackendStartResult.Failed("No UIKit view was hit at the pointer start.");
        if (hit is UIControl or UIScrollView)
        {
            return PointerSessionBackendStartResult.Failed(
                $"{hit.GetType().Name} tracks touches through UIKit's private event routing and cannot safely accept a synthetic pointer session.",
                422,
                "pointer-target-unsupported");
        }
        if (HasEnclosingScrollView(hit))
        {
            return PointerSessionBackendStartResult.Failed(
                "The target is inside a UIScrollView whose gesture arbitration cannot be reproduced safely across streamed requests.",
                422,
                "pointer-target-unsupported");
        }

        var root = window.RootViewController?.View ?? hit;
        var recognizers = CollectTouchRecognizers(hit, root);
        var rawTouch = recognizers.Count == 0 && RawTouchPolicy?.Accepts(hit) == true;
        if (recognizers.Count == 0 && !rawTouch)
        {
            return PointerSessionBackendStartResult.Failed(
                $"{hit.GetType().Name} has no supported recognizer and is not an allowed raw-touch view.",
                422,
                "pointer-target-unsupported");
        }

        var session = AppleTouchInjector.StartPointerSession(hit, recognizers, rawTouch, point);
        if (session is null)
        {
            return PointerSessionBackendStartResult.Failed(
                "A synthetic UIKit pointer could not be assembled on this OS version.",
                501,
                "not_supported");
        }

        return PointerSessionBackendStartResult.Started(
            new ApplePointerSessionState
            {
                Session = session,
                Target = hit,
                LastPoint = point
            },
            $"Synthetic UITouch pointer down on {hit.GetType().Name}");
    }

    private static PointerSessionBackendResult MoveApplePointerSession(
        object state,
        IReadOnlyList<PointerSessionSample> samples,
        CancellationToken cancellationToken)
    {
        if (state is not ApplePointerSessionState session)
            return PointerSessionBackendResult.Failed("Invalid UIKit pointer session state.", 500);

        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = new CGPoint(sample.X, sample.Y);
            if (!session.Session.MoveTo(point))
                return PointerSessionBackendResult.Failed("The synthetic UIKit pointer could not be moved.");
            session.LastPoint = point;
        }

        return PointerSessionBackendResult.Applied(
            $"Applied {samples.Count} coalesced synthetic UITouch sample(s) to {session.Target.GetType().Name}.");
    }

    private static PointerSessionBackendResult EndApplePointerSession(
        object state,
        PointerSessionSample sample,
        CancellationToken cancellationToken)
    {
        if (state is not ApplePointerSessionState session)
            return PointerSessionBackendResult.Failed("Invalid UIKit pointer session state.", 500);
        cancellationToken.ThrowIfCancellationRequested();

        var point = new CGPoint(sample.X, sample.Y);
        if (point != session.LastPoint && !session.Session.MoveTo(point))
            return PointerSessionBackendResult.Failed("The synthetic UIKit pointer could not reach its final point.");
        return session.Session.End()
            ? PointerSessionBackendResult.Applied(
                $"Synthetic UITouch pointer up on {session.Target.GetType().Name}")
            : PointerSessionBackendResult.Failed("The synthetic UIKit pointer could not end.");
    }

    private static PointerSessionBackendResult CancelApplePointerSession(object state, string reason)
    {
        if (state is not ApplePointerSessionState session)
            return PointerSessionBackendResult.Failed("Invalid UIKit pointer session state.", 500);
        return session.Session.Cancel()
            ? PointerSessionBackendResult.Applied(
                $"Synthetic UITouch pointer cancel on {session.Target.GetType().Name} ({reason}).")
            : PointerSessionBackendResult.Failed("The synthetic UIKit pointer could not be cancelled.");
    }

    private static bool HasEnclosingScrollView(UIView hit)
    {
        for (var current = hit.Superview; current is not null && current is not UIWindow; current = current.Superview)
        {
            if (current is UIScrollView)
                return true;
        }
        return false;
    }
#endif
}
