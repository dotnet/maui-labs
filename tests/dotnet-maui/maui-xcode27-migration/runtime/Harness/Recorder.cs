using System.Text.Json;
using Foundation;
using UIKit;

namespace LifecycleQualification;

internal sealed record Evidence(long Sequence, string Process, int Pid, string Delivery,
    string Kind, string? Scene, string? Window, string? Value, object? Detail);

internal static class Recorder
{
    static readonly List<Evidence> events = new();
    static readonly string process = Guid.NewGuid().ToString("N");
    static readonly object gate = new();
    internal static string Delivery { get; set; } = "os";
    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "lifecycle-events.jsonl");

    internal static void Start()
    {
        var manifestPath = NSBundle.MainBundle.PathForResource("candidate-manifest", "json");
        Emit("boot", detail: new
        {
            candidate = manifestPath is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(manifestPath)),
            platform = OperatingSystem.IsMacCatalyst() ? "maccatalyst" : "ios",
            os = NSProcessInfo.ProcessInfo.OperatingSystemVersionString,
            maui = typeof(Microsoft.Maui.MauiUISceneDelegate).Assembly.GetName().Version?.ToString(),
            events = FilePath
        });
        Console.WriteLine($"QUALIFICATION_EVENTS={FilePath}");
    }

    internal static void Emit(string kind, UIScene? scene = null, UIWindow? window = null,
        string? value = null, object? detail = null)
    {
        lock (gate)
        {
            var entry = new Evidence(events.Count + 1, process, Environment.ProcessId, Delivery, kind,
                scene?.Session.PersistentIdentifier, window?.Handle.ToString(), value, detail);
            events.Add(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var file = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(file, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(entry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            writer.Flush();
            file.Flush(true);
        }
    }

    internal static int Mark() { lock (gate) return events.Count; }
    internal static Evidence[] Since(int mark) { lock (gate) return events.Skip(mark).ToArray(); }
    internal static void Check(string name, bool pass, object? actual = null) =>
        Emit("assertion", value: name, detail: new { pass, actual });
}

// These are the original app's observable side effects. Candidate source moves calls,
// never implementations or assertions. The warm observers intentionally return false.
public static class OriginalEffects
{
    public static void SceneActivation(UIScene scene)
    {
        System.Diagnostics.Debug.WriteLine("Scene activation");
        Recorder.Emit("original.scene-activation", scene);
    }

    public static void ApplicationActivation(UIScene scene)
    {
        System.Diagnostics.Debug.WriteLine("Application activation");
        Recorder.Emit("original.application-activation", scene);
    }

    public static bool WarmUrl(UIScene scene, NSUrl url)
    {
        System.Diagnostics.Debug.WriteLine($"Received URL: {url}");
        Recorder.Emit("original.warm-url", scene, value: url.AbsoluteString);
        return false;
    }

    public static bool Activity(UIScene scene, NSUserActivity activity)
    {
        System.Diagnostics.Debug.WriteLine(activity.ActivityType);
        Recorder.Emit("original.activity", scene, value: activity.ActivityType);
        return false;
    }

    public static void ColdUrl(UIScene scene, UIWindow window, NSUrl url)
    {
        window.RootViewController!.Title = url.AbsoluteString;
        Recorder.Emit("original.cold-url", scene, window, url.AbsoluteString,
            new { title = window.RootViewController.Title });
    }

    public static void ObserveShortcut(UIApplicationShortcutItem shortcut)
    {
        System.Diagnostics.Debug.WriteLine(shortcut.Type);
        Recorder.Emit("original.shortcut-observer", value: shortcut.Type);
    }

    public static bool HandleShortcut(UIApplicationShortcutItem shortcut)
    {
        Recorder.Emit("original.shortcut-handler", value: shortcut.Type);
        return shortcut.Type == "orders.open";
    }
}
