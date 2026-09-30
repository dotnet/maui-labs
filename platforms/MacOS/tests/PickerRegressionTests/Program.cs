using System.Collections.ObjectModel;
using System.Text.Json;
using AppKit;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace PickerRegressionTests;

static class Program
{
    static void Main()
    {
        using var watchdog = new System.Threading.Timer(_ =>
        {
            Console.Error.WriteLine("FAIL: AppKit Picker regression timed out.");
            Environment.Exit(1);
        }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);

        NSApplication.Init();
        using var appDelegate = new RegressionApplication();
        NSApplication.SharedApplication.Delegate = appDelegate;
        NSApplication.SharedApplication.Run();
        GC.KeepAlive(appDelegate);
    }
}

sealed class RegressionApplication : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() =>
        MauiApp.CreateBuilder().UseMauiAppMacOS<RegressionApp>().AddMacOSEssentials().Build();

    protected override void OnStarted() => NSApplication.SharedApplication.BeginInvokeOnMainThread(RunTests);

    void RunTests()
    {
        try
        {
            var app = (RegressionApp)Application;
            var picker = app.Month;
            var native = ((PickerHandler)picker.Handler!).PlatformView;
            var evidence = Environment.GetEnvironmentVariable("PICKER_EVIDENCE") ?? "picker-evidence";
            Directory.CreateDirectory(evidence);
            Capture(native.Window.ContentView!, Path.Combine(evidence, "initial.png"));

            if (picker.SelectedIndex == -1 && picker.Title == null &&
                native.IndexOfSelectedItem == 0 && native.Title == "January")
            {
                Record("initial", picker, native, evidence);
                Console.Error.WriteLine("BASELINE_563: unselected untitled Picker displays January.");
                Environment.Exit(42);
            }

            Check("initial", -1, -1, "", picker, native, evidence);
            picker.SelectedIndex = 1;
            Check("programmatic-selection", 1, 1, "February", picker, native, evidence);
            picker.SelectedIndex = -1;
            Check("reset-selection", -1, -1, "", picker, native, evidence);

            picker.Title = "Choose month";
            Check("add-title", -1, 0, "Choose month", picker, native, evidence);
            picker.SelectedIndex = 2;
            Check("titled-selection", 2, 4, "March", picker, native, evidence);
            picker.SelectedIndex = -1;
            Check("titled-reset", -1, 0, "Choose month", picker, native, evidence);
            picker.Title = null;
            Check("remove-title", -1, -1, "", picker, native, evidence);
            picker.Title = "";
            Check("empty-title", -1, 0, "", picker, native, evidence);
            picker.Title = null;

            picker.ItemsSource = new[] { "April", "May" };
            Check("replace-items", -1, -1, "", picker, native, evidence);
            picker.ItemsSource = Array.Empty<string>();
            Check("empty-items", -1, -1, "", picker, native, evidence);
            var months = new ObservableCollection<string>();
            picker.ItemsSource = months;
            months.Add("June");
            Check("populate-items", -1, -1, "", picker, native, evidence);
            months.Add("July");
            picker.SelectedIndex = 1;
            months.Clear();
            Check("clear-selected-items", -1, -1, "", picker, native, evidence);
            months.Add("August");
            months.Add("September");
            months.Add("October");
            Check("repopulate-items", -1, -1, "", picker, native, evidence);

            Activate(native, 0);
            Check("activate-first-untitled", 0, 0, "August", picker, native, evidence);
            picker.Title = "Choose month";
            Check("title-preserves-selection", 0, 2, "August", picker, native, evidence);
            Activate(native, 4);
            Check("activate-last-titled", 2, 4, "October", picker, native, evidence);
            Activate(native, 2);
            Check("activate-first-titled", 0, 2, "August", picker, native, evidence);
            picker.Title = null;
            Check("remove-title-preserves-selection", 0, 0, "August", picker, native, evidence);
            picker.SelectedIndex = -1;
            Check("final-reset", -1, -1, "", picker, native, evidence);
            Capture(native.Window.ContentView!, Path.Combine(evidence, "final.png"));
            Console.WriteLine("PASS: all native AppKit Picker regression scenarios.");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            Environment.Exit(1);
        }
    }

    static void Activate(NSPopUpButton native, int index)
    {
        native.SelectItem(index);
        if (!NSApplication.SharedApplication.SendAction(native.Action!, native.Target, native))
            throw new InvalidOperationException("AppKit did not dispatch the Picker activation action.");
    }

    static void Check(string scenario, int selected, int nativeSelected, string title,
        Picker picker, NSPopUpButton native, string evidence)
    {
        Record(scenario, picker, native, evidence);
        if (picker.SelectedIndex != selected || native.IndexOfSelectedItem != nativeSelected ||
            native.Title != title || (nativeSelected == -1 && native.SelectedItem != null))
            throw new InvalidOperationException(
                $"{scenario}: expected managed={selected}, native={nativeSelected}, title='{title}'; " +
                $"actual managed={picker.SelectedIndex}, native={native.IndexOfSelectedItem}, title='{native.Title}'.");
        if (selected == -1 && picker.SelectedItem != null)
            throw new InvalidOperationException($"{scenario}: unselected Picker retains a managed SelectedItem.");
        Console.WriteLine($"PASS: {scenario}");
    }

    static void Record(string scenario, Picker picker, NSPopUpButton native, string evidence) =>
        File.AppendAllText(Path.Combine(evidence, "states.jsonl"), JsonSerializer.Serialize(new
        {
            scenario,
            managedIndex = picker.SelectedIndex,
            managedItem = picker.SelectedItem,
            nativeIndex = (int)native.IndexOfSelectedItem,
            nativeTitle = native.Title,
            nativeItem = native.SelectedItem?.Title,
            pickerTitle = picker.Title
        }) + Environment.NewLine);

    static void Capture(NSView view, string path)
    {
        view.LayoutSubtreeIfNeeded();
        view.DisplayIfNeeded();
        using var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds)
            ?? throw new InvalidOperationException("AppKit could not allocate screenshot bitmap.");
        view.CacheDisplay(view.Bounds, bitmap);
        using var properties = new NSDictionary();
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, properties)
            ?? throw new InvalidOperationException("AppKit could not encode screenshot.");
        File.WriteAllBytes(path, png.ToArray());
    }
}

public sealed class RegressionApp : Application
{
    public Picker Month { get; } = new()
    {
        ItemsSource = new[] { "January", "February", "March" },
        AutomationId = "UnselectedMonth"
    };

    protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
        new(new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                Children =
                {
                    new Label { Text = "Month (initially no selection and no title)" },
                    Month
                }
            }
        })
        {
            Title = "AppKit Picker regression",
            Width = 480,
            Height = 240
        };
}
