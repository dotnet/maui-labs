using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AppKit;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace MacOS.RuntimeTests.Scenarios;

sealed class DialogScenario : MauiRuntimeScenario
{
    readonly TaskCompletionSource _completion = new();
    NSTimer? _timer;
    Task? _pending;
    Func<bool>? _verifyResult;
    long _stageStarted;
    int _stage;
    bool _observedDialog;

    [ModuleInitializer]
    public static void Register() => ScenarioRegistry.Register(
        new("dialogs", ExpectedCases: 3,
            CreateDelegate: context => new DialogScenario().CreateDelegate(context),
            ExpectedAssertions: 10, TimeoutSeconds: 45));

    public override Window CreateWindow(IActivationState? activationState) =>
        new(new ContentPage { Content = new Label { Text = "Native Page dialog scenario" } })
        { Title = "Dialog subscription runtime tests", Width = 500, Height = 300 };

    public override Task RunAsync(RuntimeTestContext context, Window window)
    {
        var assembly = typeof(Window).Assembly;
        var subscriptionType = assembly.GetType("Microsoft.Maui.Controls.Platform.AlertManager", true)!
            .GetProperty("Subscription")?.PropertyType
            ?? throw new InvalidOperationException("MAUI has no AlertManager.Subscription property.");
        var subscription = window.Handler!.MauiContext!.Services.GetService(subscriptionType);
        context.WriteJson("registration.json", new
        {
            assembly = assembly.FullName,
            version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            consumedType = subscriptionType.FullName,
            registeredType = subscription?.GetType().FullName
        });
        context.Assert(subscription is DispatchProxy,
            "The real window must resolve the consumed dialog subscription.", "dialogs.subscription");

        _stageStarted = Stopwatch.GetTimestamp();
        _timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ => Tick(context, window));
        return _completion.Task;
    }

    void Tick(RuntimeTestContext context, Window window)
    {
        try
        {
            if (Stopwatch.GetElapsedTime(_stageStarted) > TimeSpan.FromSeconds(10))
                throw new TimeoutException($"Dialog stage {_stage}: task={_pending?.Status}; visible={_observedDialog}.");

            var nativeWindow = (NSWindow)window.Handler!.PlatformView!;
            var page = window.Page!;
            var name = _stage switch { 0 => "action-sheet", 1 => "prompt", _ => "alert" };

            if (_pending is null)
            {
                if (nativeWindow.AttachedSheet is not null)
                    return;
                _observedDialog = false;
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
                var matches = buttons.Where(b => b.Title == expectedTitle).ToArray();
                context.AppendJson("dialogs.jsonl", new { name, visible = true, buttons = buttons.Select(b => b.Title).ToArray() });
                context.Assert(matches.Length == 1, $"{name}: exactly one native {expectedTitle} button.");
                context.Capture(sheet.ContentView!, $"{name}.png");
                _observedDialog = true;
                matches[0].PerformClick(matches[0]);
            }

            if (_pending!.IsCompleted)
            {
                _pending.GetAwaiter().GetResult();
                context.Assert(_observedDialog, $"{name}: observed a visible native sheet.");
                context.Assert(_verifyResult!(), $"{name}: Page task returned the expected result.");
                context.Pass(name);
                _pending = null;
                if (++_stage == 3)
                {
                    StopTimer();
                    _completion.SetResult();
                }
                else
                {
                    _stageStarted = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (Exception ex)
        {
            StopTimer();
            _completion.SetException(ex);
        }
    }

    void Start<T>(Task<T> task, T expected)
    {
        _pending = task;
        _verifyResult = () => EqualityComparer<T>.Default.Equals(task.GetAwaiter().GetResult(), expected);
    }

    void StopTimer()
    {
        _timer?.Invalidate();
        _timer?.Dispose();
        _timer = null;
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
