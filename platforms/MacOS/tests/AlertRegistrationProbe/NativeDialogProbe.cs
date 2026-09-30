using AppKit;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

sealed class NativeDialogProbe : MacOSMauiApplication
{
    NSTimer? _timer;
    Task? _pending;
    Func<bool>? _verifyResult;
    DateTime _requestedAt;
    int _stage;
    bool _observedDialog;

    protected override MauiApp CreateMauiApp() =>
        MauiApp.CreateBuilder().UseMauiAppMacOS<ProbeApp>().Build();

    protected override void OnStarted()
    {
        _timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ => Tick());
    }

    void Tick()
    {
        try
        {
            var window = (Window)Windows[0];
            var nativeWindow = (NSWindow)window.Handler!.PlatformView!;
            var page = window.Page!;

            if (_pending is null)
            {
                if (nativeWindow.AttachedSheet is not null)
                    return;
                _observedDialog = false;
                _requestedAt = DateTime.UtcNow;
                switch (_stage)
                {
                    case 0:
                        Start(page.DisplayActionSheetAsync("Subscription action sheet", "Cancel", null, "One", "Two"), "One");
                        break;
                    case 1:
                        Start(page.DisplayPromptAsync("Subscription prompt", "Enter text", initialValue: "probe"), "probe");
                        break;
                    default:
                        Start(page.DisplayAlertAsync("Subscription alert", "Continue?", "Yes", "No"), true);
                        break;
                }
            }

            if (!_observedDialog && nativeWindow.AttachedSheet is NSWindow sheet && sheet.IsVisible)
            {
                var buttons = Descendants(sheet.ContentView!).OfType<NSButton>().ToArray();
                var expectedTitle = _stage switch { 0 => "One", 1 => "OK", _ => "Yes" };
                var button = buttons.Single(b => b.Title == expectedTitle);
                Console.WriteLine($"VISIBLE stage {_stage}: {string.Join(", ", buttons.Select(b => b.Title))}");
                _observedDialog = true;
                button.PerformClick(button);
            }

            if (_pending!.IsCompleted)
            {
                _pending.GetAwaiter().GetResult();
                if (!_observedDialog || !_verifyResult!())
                    throw new InvalidOperationException($"Stage {_stage}: native dialog/result did not match.");

                Console.WriteLine($"PASS native dialog stage {_stage}");
                _pending = null;
                if (++_stage == 3)
                {
                    _timer!.Invalidate();
                    Console.WriteLine("PASS all native AppKit Page dialogs");
                    Environment.Exit(0);
                }
            }
            else if (DateTime.UtcNow - _requestedAt > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException($"Stage {_stage}: Page dialog task {_pending.Status}; visible={_observedDialog}.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _timer?.Invalidate();
            Environment.Exit(1);
        }
    }

    void Start<T>(Task<T> task, T expected)
    {
        _pending = task;
        _verifyResult = () => EqualityComparer<T>.Default.Equals(task.GetAwaiter().GetResult(), expected);
    }

    static IEnumerable<NSView> Descendants(NSView view)
    {
        foreach (var child in view.Subviews)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}

public sealed class ProbeApp : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new ContentPage { Content = new Label { Text = "Native dialog subscription regression probe" } })
        { Title = "Dialog subscription probe", Width = 500, Height = 300 };
}
