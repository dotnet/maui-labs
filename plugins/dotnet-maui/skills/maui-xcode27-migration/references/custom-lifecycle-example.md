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
        foreach (var context in connectionOptions.UrlContexts?.ToArray<UIOpenUrlContext>() ?? [])
        {
            var url = context.Url;
            pending.Enqueue(window => HandleColdLaunchUrl(window, url));
        }
        foreach (var activity in connectionOptions.UserActivities?.ToArray<NSUserActivity>() ?? [])
            pending.Enqueue(_ => HandleOriginalActivity(scene, activity));

        base.WillConnect(scene, session, connectionOptions);
    }

    public override void OnActivated(UIScene scene)
    {
        base.OnActivated(scene);
        HandleExistingSceneActivation(scene);
        HandleApplicationActivation(scene);
        if (Window is not UIWindow window)
            return; // Work remains queued until this scene has a window.
        while (pending.Count > 0)
            pending.Dequeue()(window);
    }

    public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> contexts)
    {
        bool handled = base.OpenUrl(scene, contexts);
        foreach (var context in contexts.ToArray<UIOpenUrlContext>())
            handled = HandleOriginalWarmUrl(scene, context.Url) | handled;
        return handled;
    }

    public override bool ContinueUserActivity(UIScene scene, NSUserActivity activity)
    {
        bool forwarded = base.ContinueUserActivity(scene, activity);
        return HandleOriginalActivity(scene, activity) | forwarded;
    }

    protected abstract void HandleExistingSceneActivation(UIScene scene);
    protected abstract void HandleApplicationActivation(UIScene scene);
    protected abstract void HandleColdLaunchUrl(UIWindow window, NSUrl url);
    protected abstract bool HandleOriginalWarmUrl(UIScene scene, NSUrl url);
    protected abstract bool HandleOriginalActivity(UIScene scene, NSUserActivity activity);
}
```

The two activation calls deliberately remain separate: one contains the custom
scene delegate's existing implementation, the other the former AppDelegate
implementation. Preserve both bodies, including their original logging;
replacing two observers with one combined log loses a side effect.
`base.OnActivated` supplies MAUI behavior, not the custom code being moved or
replaced. Likewise, map the original warm URL/activity handlers and cold-launch
work to the corresponding methods. Do not leave the AppDelegate activation
override as the only delivery path because it predates this task.

The URL helpers also have distinct owners. Move the original AppDelegate
`OpenUrl` app-specific body into `HandleOriginalWarmUrl`; move the old
FinishedLaunching URL/window initialization into `HandleColdLaunchUrl`.
For a warm URL observer, retain its original logging even when no window is
available. If the old override only logged and returned the base result,
`HandleOriginalWarmUrl` must return **false** so the outer override keeps that
base result. Returning true after logging changes an unhandled URL into a
handled one. Setting a cold-launch window title is not a replacement for that
observer, and cold-only initialization must not be added to warm delivery.
Copy the original app behavior into these slots rather than inventing a new
router from their names.

To apply the abstract example, copy the existing statements into the matching
helpers first, then connect the callbacks shown above. Remove slots that have
no original behavior rather than inventing a route from a helper's name.
Finally trace each connection-option loop through the queue drain to its
implemented body. A comment-only title block or an undefined navigation helper
is not a completed transformation, even if the accompanying mapping says it is.

For activity overrides, the app helper still returns its original handled
result (false for an observer). The override combines that result with the
unconditional base result; a true Essentials/base result must not be discarded
merely to force the entire override to return false. A Scene* registration
returns only its own handled result instead.

Warm and cold activities deliberately call the **same** original activity
handler in the pattern. Under the application lifecycle, ContinueUserActivity can deliver
an activity during a cold launch; under scenes, initial activities arrive in
connection options. The absence of a UserActivities loop in the old
FinishedLaunching does not mean there was no cold activity behavior to retain.
An empty enumeration or a separate unimplemented cold hook loses that delivery.

Window existence is enough for native title work, but not necessarily for
Shell navigation. If navigation initializes asynchronously, drain at the
app's actual navigation-ready signal instead; retain the same per-scene queue.
Keep validation of recognized URLs/activities and repository-standard error
handling inside the real handlers. Do not mark unknown links handled merely
because they were observed.

Before claiming completion, enumerate the original callbacks again and point
to each actual implementation in the edited code. An unmapped callback is a
failed migration even when its old method still exists or the app compiles.
