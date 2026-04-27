using Foundation;
using CoreFoundation;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

/// <summary>
/// MainThread helper for macOS. Use instead of Microsoft.Maui.ApplicationModel.MainThread
/// which throws NotImplementedException on unsupported platforms.
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
