using System.Runtime.CompilerServices;
using AppKit;
using CoreFoundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;

namespace MacOS.RuntimeTests.Scenarios.MediaCapture;

sealed class MediaCaptureScenario : MauiRuntimeScenario
{
    [ModuleInitializer]
    public static void Register() => ScenarioRegistry.Register(new(
        "media-capture", ExpectedCases: 1,
        CreateDelegate: context => new MediaCaptureScenario().CreateDelegate(context),
        ExpectedAssertions: 4,
        TimeoutSeconds: 45));

    public override Window CreateWindow(IActivationState? activationState) => new(new ContentPage
    {
        Content = new Label
        {
            Text = "Native camera capture regression",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        },
    });

    public override async Task RunAsync(RuntimeTestContext context, Window window)
    {
        var services = window.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("The runtime window has no MAUI services.");
        var picker = services.GetRequiredService<IMediaPicker>();
        context.Assert(picker.IsCaptureSupported,
            "The camera-equipped runner must report native media capture support.");

        var stopTask = ClickStopWhenAvailableAsync();
        var result = await picker.CaptureVideoAsync(new MediaPickerOptions
        {
            Title = "AppKit camera regression",
        });
        await stopTask;

        context.Assert(result is not null, "Stopping the recording must return a media file.");
        context.Assert(File.Exists(result!.FullPath), "The recorded movie must exist.");
        context.Assert(new FileInfo(result.FullPath).Length > 0, "The recorded movie must be non-empty.");
        context.WriteJson("media-capture.json", new
        {
            result.FileName,
            result.FullPath,
            bytes = new FileInfo(result.FullPath).Length,
        });
        File.Delete(result.FullPath);
        context.Pass("native video capture");
    }

    static async Task ClickStopWhenAvailableAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(100);
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                var application = NSApplication.SharedApplication;
                var button = new[] { application.KeyWindow, application.MainWindow }
                    .OfType<NSWindow>()
                    .Distinct()
                    .SelectMany(window => Descendants(window.ContentView))
                    .OfType<NSButton>()
                    .FirstOrDefault(candidate => candidate.Title == "Stop Recording");
                if (button is not null)
                    button.PerformClick(button);
                completion.TrySetResult(button is not null);
            });
            if (await completion.Task)
                return;
        }

        throw new TimeoutException("The AppKit recording Stop button did not appear.");
    }

    static IEnumerable<NSView> Descendants(NSView? root)
    {
        if (root is null)
            yield break;

        yield return root;
        foreach (var child in root.Subviews)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
