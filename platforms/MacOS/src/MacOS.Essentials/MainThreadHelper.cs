using Foundation;
using CoreFoundation;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

/// <summary>
/// Main-thread helper that uses MAUI's dispatcher bridge and retains a fallback for
/// applications running against older MAUI 10 packages.
/// </summary>
public static class MainThreadHelper
{
    public static bool IsMainThread
    {
        get
        {
#if NET11_0_OR_GREATER
            return MainThread.IsMainThread;
#else
            try
            {
                return MainThread.IsMainThread;
            }
            catch (FeatureNotSupportedException)
            {
                return NSThread.Current.IsMainThread;
            }
            catch (Exception ex) when (ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
            {
                return NSThread.Current.IsMainThread;
            }
#endif
        }
    }

    public static void BeginInvokeOnMainThread(Action action)
    {
#if NET11_0_OR_GREATER
        MainThread.BeginInvokeOnMainThread(action);
#else
        bool useMauiMainThread;
        try
        {
            _ = MainThread.IsMainThread;
            useMauiMainThread = true;
        }
        catch (FeatureNotSupportedException)
        {
            useMauiMainThread = false;
        }
        catch (Exception ex) when (ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            useMauiMainThread = false;
        }

        if (useMauiMainThread)
            MainThread.BeginInvokeOnMainThread(action);
        else if (NSThread.Current.IsMainThread)
            action();
        else
            DispatchQueue.MainQueue.DispatchAsync(action);
#endif
    }
}
