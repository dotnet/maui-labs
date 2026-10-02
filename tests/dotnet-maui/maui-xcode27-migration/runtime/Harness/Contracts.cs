using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using MyApp;
using UIKit;

namespace LifecycleQualification;

internal static class Contracts
{
    static readonly Dictionary<string, UIOpenUrlContext> receivedContexts = new();
    static readonly HashSet<string> activatedScenes = new();
    static bool baseHandled;
    static bool running;

    internal static void ObserveBaseForwarding(MauiAppBuilder builder) =>
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
            .SceneWillConnect((scene, session, options) =>
                Recorder.Emit("base.connect", scene, detail: new
                {
                    urlsAbsent = options.UrlContexts is null,
                    activitiesAbsent = options.UserActivities is null,
                    urls = (options.UrlContexts?.ToArray<UIOpenUrlContext>() ?? []).Select(c => c.Url.AbsoluteString).ToArray(),
                    activities = (options.UserActivities?.ToArray<NSUserActivity>() ?? []).Select(a => a.ActivityType).ToArray()
                }))
            .SceneOnActivated(scene =>
            {
                if (Recorder.Delivery == "os")
                    activatedScenes.Add(scene.Session.PersistentIdentifier);
                Recorder.Emit("base.activate", scene, (scene.Delegate as ExistingSceneDelegate)?.Window);
            })
            .SceneOpenUrl((scene, contexts) =>
            {
                Recorder.Emit("base.url", scene, detail: new
                {
                    urls = contexts.ToArray<UIOpenUrlContext>().Select(c => c.Url.AbsoluteString).ToArray(),
                    result = baseHandled
                });
                if (Recorder.Delivery == "os")
                {
                    foreach (var context in contexts.ToArray<UIOpenUrlContext>())
                    {
                        var url = context.Url.AbsoluteString!;
                        if (url.Contains("/warm-a?", StringComparison.Ordinal) ||
                            url.Contains("/warm-b?", StringComparison.Ordinal))
                            receivedContexts[url] = context;
                        if (url.Contains("/contracts?", StringComparison.Ordinal) && !running)
                        {
                            running = true;
                            Application.Current!.Dispatcher.Dispatch(() => _ = RunAsync(url));
                        }
                    }
                }
                return baseHandled;
            })
            .SceneContinueUserActivity((scene, activity) =>
            {
                Recorder.Emit("base.activity", scene, value: activity.ActivityType, detail: new { result = baseHandled });
                return baseHandled;
            })));

    static async Task RunAsync(string command)
    {
        try
        {
            if (Scenes().Length < 2)
                Application.Current!.OpenWindow(QualificationApp.NewWindow());
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (Scenes().Length < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            var scenes = Scenes();
            Recorder.Delivery = "injected-contract";
            Recorder.Check("two-real-window-scenes", scenes.Length >= 2,
                scenes.Select(s => s.Session.PersistentIdentifier).ToArray());
            if (scenes.Length < 2)
                return;
            var contexts = receivedContexts.Values.ToArray();
            Recorder.Check("two-os-captured-url-contexts", contexts.Length == 2, contexts.Length);
            if (contexts.Length != 2)
                return;

            var first = scenes[0];
            var second = scenes[1];
            WarmContracts(first, contexts);
            ColdContracts(first, second, contexts);
            await ShortcutContracts(first);
        }
        catch (Exception error)
        {
            Recorder.Emit("contract.error", value: error.ToString());
        }
        finally
        {
            baseHandled = false;
            Recorder.Emit("contract.complete", value: command);
            Recorder.Delivery = "os";
        }
    }

    static UIWindowScene[] Scenes() => UIApplication.SharedApplication.ConnectedScenes
        .ToArray<UIScene>().OfType<UIWindowScene>()
        .Where(s => activatedScenes.Contains(s.Session.PersistentIdentifier) &&
            s.Delegate is ExistingSceneDelegate { Window.RootViewController: not null })
        .OrderBy(s => s.Session.PersistentIdentifier, StringComparer.Ordinal).ToArray();

    static void WarmContracts(UIWindowScene scene, UIOpenUrlContext[] contexts)
    {
        var candidate = (ExistingSceneDelegate)scene.Delegate!;
        var window = candidate.Window!;
        var title = window.RootViewController!.Title;
        using var set = new NSSet<UIOpenUrlContext>(contexts);
        using var activity = new NSUserActivity("qualification.warm");
        foreach (bool expected in new[] { false, true })
        {
            baseHandled = expected;
            var mark = Recorder.Mark();
            bool urlResult = candidate.OpenUrl(scene, set);
            bool activityResult = candidate.ContinueUserActivity(scene, activity);
            var events = Recorder.Since(mark);
            Recorder.Check($"warm-result-{expected.ToString().ToLowerInvariant()}",
                urlResult == expected && activityResult == expected,
                new { urlResult, activityResult });
            Recorder.Check($"warm-forwarding-{expected.ToString().ToLowerInvariant()}",
                events.Count(e => e.Kind == "base.url") == 1 &&
                events.Count(e => e.Kind == "base.activity") == 1, events);
            Recorder.Check($"warm-observers-{expected.ToString().ToLowerInvariant()}",
                contexts.All(c => events.Count(e => e.Kind == "original.warm-url" && e.Value == c.Url.AbsoluteString) == 1) &&
                events.Count(e => e.Kind == "original.activity" && e.Value == activity.ActivityType) == 1 &&
                events.All(e => e.Kind != "original.cold-url") &&
                window.RootViewController.Title == title, events);
        }
        baseHandled = false;
        try
        {
            candidate.Window = null;
            var mark = Recorder.Mark();
            var handled = candidate.OpenUrl(scene, set);
            var events = Recorder.Since(mark);
            Recorder.Check("warm-observer-without-window",
                !handled && contexts.All(c => events.Count(e => e.Kind == "original.warm-url" &&
                    e.Value == c.Url.AbsoluteString) == 1), events);
        }
        finally { candidate.Window = window; }
    }

    static void ColdContracts(UIWindowScene first, UIWindowScene second, UIOpenUrlContext[] contexts)
    {
        var a = (ExistingSceneDelegate)first.Delegate!;
        var b = (ExistingSceneDelegate)second.Delegate!;
        var windowA = a.Window!;
        var windowB = b.Window!;
        using var activityA = new NSUserActivity("qualification.cold.a");
        using var activityB = new NSUserActivity("qualification.cold.b");
        var mark = Recorder.Mark();
        try
        {
            a.Window = null;
            b.Window = null;
            a.CaptureCold(first, contexts, new[] { activityA, activityB });
            b.CaptureCold(second, contexts.Reverse(), new[] { activityB, activityA });
            a.OnActivated(first);
            a.OnActivated(first);
            b.OnActivated(second);
            var pending = Recorder.Since(mark);
            Recorder.Check("cold-pending-without-window",
                pending.All(e => e.Kind is not ("original.cold-url" or "original.activity")), pending);
            Recorder.Check("both-original-activations",
                pending.Count(e => e.Kind == "original.scene-activation" && e.Scene == first.Session.PersistentIdentifier) == 2 &&
                pending.Count(e => e.Kind == "original.application-activation" && e.Scene == first.Session.PersistentIdentifier) == 2 &&
                pending.Count(e => e.Kind == "original.scene-activation" && e.Scene == second.Session.PersistentIdentifier) == 1 &&
                pending.Count(e => e.Kind == "original.application-activation" && e.Scene == second.Session.PersistentIdentifier) == 1, pending);

            a.Window = windowA;
            var drainA = Recorder.Mark();
            a.OnActivated(first);
            var eventsA = Recorder.Since(drainA);
            CheckDrain("cold-drain-first", first, windowA, contexts, eventsA);
            Recorder.Check("other-window-stays-pending", eventsA.All(e =>
                e.Scene != second.Session.PersistentIdentifier &&
                (e.Kind != "original.cold-url" || e.Window == windowA.Handle.ToString())), eventsA);
            var titleA = windowA.RootViewController!.Title;
            b.Window = windowB;
            var drainB = Recorder.Mark();
            b.OnActivated(second);
            CheckDrain("cold-drain-second", second, windowB, contexts, Recorder.Since(drainB));
            Recorder.Check("window-isolation",
                windowA.Handle != windowB.Handle && windowA.RootViewController.Title == titleA &&
                titleA == contexts[^1].Url.AbsoluteString &&
                windowB.RootViewController!.Title == contexts[0].Url.AbsoluteString,
                new { first = windowA.RootViewController.Title, second = windowB.RootViewController!.Title });
            var repeat = Recorder.Mark();
            a.OnActivated(first);
            b.OnActivated(second);
            var repeated = Recorder.Since(repeat);
            Recorder.Check("cold-drained-exactly-once",
                repeated.All(e => e.Kind is not ("original.cold-url" or "original.activity")), repeated);
        }
        finally
        {
            a.Window = windowA;
            b.Window = windowB;
        }
    }

    static void CheckDrain(string name, UIScene scene, UIWindow window, UIOpenUrlContext[] contexts, Evidence[] events)
    {
        Recorder.Check(name,
            events.Count(e => e.Kind == "base.activate") == 1 &&
            events.Count(e => e.Kind == "original.cold-url") == contexts.Length &&
            events.Count(e => e.Kind == "original.activity") == 2 &&
            contexts.All(c => events.Count(e => e.Kind == "original.cold-url" && e.Value == c.Url.AbsoluteString &&
                e.Scene == scene.Session.PersistentIdentifier && e.Window == window.Handle.ToString()) == 1) &&
            new[] { "qualification.cold.a", "qualification.cold.b" }.All(type =>
                events.Count(e => e.Kind == "original.activity" && e.Value == type &&
                    e.Scene == scene.Session.PersistentIdentifier) == 1), events);
    }

    static async Task ShortcutContracts(UIWindowScene scene)
    {
        var candidate = (ExistingSceneDelegate)scene.Delegate!;
        var lifecycle = IPlatformApplication.Current!.Services.GetRequiredService<ILifecycleEventService>();
        var registrations = lifecycle.GetEventDelegates<iOSLifecycle.PerformActionForShortcutItem>(
            nameof(iOSLifecycle.PerformActionForShortcutItem)).ToArray();
        foreach (var type in new[] { "orders.open", "qualification.unknown" })
        {
            using var shortcut = new UIApplicationShortcutItem(type, type);
            var custom = new List<object>();
            int observers = 0, handlers = 0;
            bool correct = true;
            foreach (var registration in registrations)
            {
                var replies = new List<bool>();
                var mark = Recorder.Mark();
                registration(UIApplication.SharedApplication, shortcut, result => replies.Add(result));
                await Task.Delay(100);
                var events = Recorder.Since(mark);
                int observed = events.Count(e => e.Kind == "original.shortcut-observer");
                int handled = events.Count(e => e.Kind == "original.shortcut-handler");
                observers += observed;
                handlers += handled;
                if (observed + handled == 0)
                    continue; // MAUI/Essentials registrations are not the two custom registrations.
                bool expected = handled == 1 && type == "orders.open";
                correct &= observed + handled == 1 && replies.Count == 1 && replies[0] == expected;
                custom.Add(new { observed, handled, replies });
            }
            Recorder.Check($"shortcut-individual-{type}", correct && observers == 1 && handlers == 1, custom);
            var completions = new List<bool>();
            var aggregateMark = Recorder.Mark();
            candidate.PerformAction(scene, shortcut, result => completions.Add(result));
            await Task.Delay(250);
            var aggregateEvents = Recorder.Since(aggregateMark);
            Recorder.Check($"shortcut-aggregate-{type}",
                completions.Count == 1 && completions[0] == (type == "orders.open") &&
                aggregateEvents.Count(e => e.Kind == "original.shortcut-observer") == 1 &&
                aggregateEvents.Count(e => e.Kind == "original.shortcut-handler") == 1,
                new { completions, events = aggregateEvents });
        }
    }
}
