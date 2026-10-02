using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using UIKit;
using LifecycleQualification;

namespace MyApp;

// This is the replaceable reference control, not a test assertion.
[Register("ExistingSceneDelegate")]
public class ExistingSceneDelegate : MauiUISceneDelegate
{
    readonly Queue<Action<UIWindow>> pending = new();

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options)
    {
        CaptureCold(scene, options.UrlContexts?.ToArray<UIOpenUrlContext>() ?? [],
            options.UserActivities?.ToArray<NSUserActivity>() ?? []);
        base.WillConnect(scene, session, options);
    }

    // The injection seam uses real UIKit objects already delivered by the OS. It cannot
    // establish that UIKit supplied several contexts in one native connection callback.
    public void CaptureCold(UIScene scene, IEnumerable<UIOpenUrlContext> urls, IEnumerable<NSUserActivity> activities)
    {
        foreach (var context in urls)
        {
            var url = context.Url;
            pending.Enqueue(window => OriginalEffects.ColdUrl(scene, window, url));
        }
        foreach (var activity in activities)
            pending.Enqueue(_ => OriginalEffects.Activity(scene, activity));
    }

    public override void OnActivated(UIScene scene)
    {
        base.OnActivated(scene);
        OriginalEffects.SceneActivation(scene);
        OriginalEffects.ApplicationActivation(scene);
        if (Window is not UIWindow window)
            return;
        while (pending.Count > 0)
            pending.Dequeue()(window);
    }

    public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> contexts)
    {
        bool handled = base.OpenUrl(scene, contexts);
        foreach (var context in contexts.ToArray<UIOpenUrlContext>())
            handled = OriginalEffects.WarmUrl(scene, context.Url) | handled;
        return handled;
    }

    public override bool ContinueUserActivity(UIScene scene, NSUserActivity activity)
    {
        bool handled = base.ContinueUserActivity(scene, activity);
        return OriginalEffects.Activity(scene, activity) | handled;
    }
}

public static class MigrationRegistration
{
    public static void Configure(MauiAppBuilder builder) =>
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
            .PerformActionForShortcutItem((application, shortcut, completion) =>
            {
                OriginalEffects.ObserveShortcut(shortcut);
                completion(false);
            })
            .PerformActionForShortcutItem((application, shortcut, completion) =>
            {
                completion(OriginalEffects.HandleShortcut(shortcut));
            })));
}
