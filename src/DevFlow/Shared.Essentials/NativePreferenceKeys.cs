#if IOS || MACCATALYST || MACOS
using Foundation;
#endif

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class NativePreferenceKeys
{
    public static IReadOnlyCollection<string>? Enumerate(string? sharedName)
    {
#if IOS || MACCATALYST || MACOS
        // Enumerate only the app's persistent domain, not global/system defaults.
        var domain = !string.IsNullOrWhiteSpace(sharedName)
            ? sharedName
            : NSBundle.MainBundle.BundleIdentifier;
        if (string.IsNullOrEmpty(domain))
            return null;

        using var representation = NSUserDefaults.StandardUserDefaults.PersistentDomainForName(domain);
        if (representation is null)
            return Array.Empty<string>();

        return representation.Keys.Select(key => key.ToString()).ToArray();
#elif ANDROID
        // Mirror Essentials' default or private named SharedPreferences store.
        var context = global::Android.App.Application.Context;
        using var prefs = string.IsNullOrWhiteSpace(sharedName)
#pragma warning disable CS0618, CA1422 // Match Essentials' supported default store, including on Android 29+.
            ? global::Android.Preferences.PreferenceManager.GetDefaultSharedPreferences(context)
#pragma warning restore CS0618, CA1422
            : context.GetSharedPreferences(sharedName, global::Android.Content.FileCreationMode.Private);
        var all = prefs?.All
            ?? throw new InvalidOperationException("The Android preference store is unavailable.");
        return new List<string>(all.Keys);
#else
        return null;
#endif
    }
}
