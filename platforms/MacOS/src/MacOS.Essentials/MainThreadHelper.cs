using Foundation;
using CoreFoundation;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

/// <summary>
/// AppKit main-thread helper that works without a built <c>MauiApp</c>.
/// After <c>UseMauiAppMacOS</c> builds the app, <c>Microsoft.Maui.ApplicationModel.MainThread</c>
/// also works: MAUI bridges it from the macOS application dispatcher.
/// </summary>
public static class MainThreadHelper
{
    public static bool IsMainThread => NSThread.Current.IsMainThread;

    public static void BeginInvokeOnMainThread(Action action)
    {
        if (IsMainThread)
            action();
        else
            DispatchQueue.MainQueue.DispatchAsync(action);
    }
}
