# Additive custom-lifecycle migration

Use this pattern when custom callbacks exist. First map each original side
effect to its destination. Leftover AppDelegate callbacks in an app that
already has a scene manifest are unfinished migration work: reconcile their
intended behavior, without attributing their earlier inactivity to this patch.
Do not replace a custom delegate wholesale with this example.

## Preserve each lifecycle registration

Keep an existing shortcut observer and the moved custom handler as **separate**
registrations. Both own a completion; neither calls Essentials a second time:

```csharp
builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
    .PerformActionForShortcutItem((application, shortcut, completion) =>
    {
        ObserveShortcut(shortcut); // Existing observer's side effects.
        completion(false);
    })
    .PerformActionForShortcutItem((application, shortcut, completion) =>
    {
        bool handled = HandleShortcut(shortcut); // Existing custom decision.
        completion(handled);
    })
    .SceneContinueUserActivity((scene, activity) =>
    {
        return HandleActivity(scene, activity); // Existing activity handler.
    })));
```

The three helper names above stand for the app's existing implementations,
not APIs to invent or empty methods to add. Adapt arguments deliberately.
Preserve the old activity handler's actual handled result. For an existing
logging-only activity handler, retain its logging and return false.
If using the scene override below instead, do not also register the same
activity side effect here.

Moving an existing ContinueUserActivity handler to SceneContinueUserActivity
does not deliver cold universal links. The cold `UserActivities` loop below
must route to that same app-specific behavior after window readiness; it is
not optional just because the old FinishedLaunching implementation only read a
custom-scheme URL. Base scene connection is not a replay of all app activities.

## Capture cold work per scene; drain only with that scene's window

This compilable abstract example forces the consuming app to supply its real
behavior. It illustrates members to merge into an existing scene delegate,
not a new registered delegate to install in addition to the old one.
Keep existing registration/configuration names and original scene behavior.
There is no static/global queue shared across windows.

```csharp
using System;
using System.Collections.Generic;
using Foundation;
using Microsoft.Maui;
using UIKit;

public abstract class LinkAwareSceneDelegate : MauiUISceneDelegate
{
    readonly Queue<Action<UIWindow>> pending = new();

    public override void WillConnect(UIScene scene, UISceneSession session,
        UISceneConnectionOptions connectionOptions)
    {
        foreach (var context in connectionOptions.UrlContexts.ToArray<UIOpenUrlContext>())
        {
            var url = context.Url;
            pending.Enqueue(window => HandleColdUrl(window, url));
        }
        foreach (var activity in connectionOptions.UserActivities.ToArray<NSUserActivity>())
            pending.Enqueue(window => HandleColdActivity(window, activity));

        base.WillConnect(scene, session, connectionOptions);
    }

    public override void OnActivated(UIScene scene)
    {
        base.OnActivated(scene);
        HandleActivation(scene);
        if (Window is not UIWindow window)
            return; // Work remains queued until this scene has a window.
        while (pending.Count > 0)
            pending.Dequeue()(window);
    }

    public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> contexts)
    {
        bool handled = base.OpenUrl(scene, contexts);
        foreach (var context in contexts.ToArray<UIOpenUrlContext>())
            handled = HandleWarmUrl(scene, context.Url) | handled;
        return handled;
    }

    public override bool ContinueUserActivity(UIScene scene, NSUserActivity activity)
    {
        bool forwarded = base.ContinueUserActivity(scene, activity);
        return HandleWarmActivity(scene, activity) | forwarded;
    }

    protected abstract void HandleActivation(UIScene scene);
    protected abstract void HandleColdUrl(UIWindow window, NSUrl url);
    protected abstract void HandleColdActivity(UIWindow window, NSUserActivity activity);
    protected abstract bool HandleWarmUrl(UIScene scene, NSUrl url);
    protected abstract bool HandleWarmActivity(UIScene scene, NSUserActivity activity);
}
```

`HandleActivation` must preserve **both** the former AppDelegate activation
side effects and the custom scene delegate's existing activation side effects.
Likewise, map the original warm URL/activity handlers and cold-launch work to
the corresponding methods. Do not leave the AppDelegate activation override
as the only delivery path because it predates this task.

Window existence is enough for native title work, but not necessarily for
Shell navigation. If navigation initializes asynchronously, drain at the
app's actual navigation-ready signal instead; retain the same per-scene queue.
Keep validation of recognized URLs/activities and repository-standard error
handling inside the real handlers. Do not mark unknown links handled merely
because they were observed.

Before claiming completion, enumerate the original callbacks again and point
to each actual implementation in the edited code. An unmapped callback is a
failed migration even when its old method still exists or the app compiles.
