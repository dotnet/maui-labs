using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace MacOS.RuntimeTests.Scenarios.Picker;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("picker", 19, context => new PickerScenario().CreateDelegate(context),
            ExpectedAssertions: 31));
}

sealed class PickerScenario : MauiRuntimeScenario
{
    Microsoft.Maui.Controls.Picker _month = null!;

    public override Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var picker = _month;
        var native = (picker.Handler as PickerHandler)?.PlatformView
            ?? throw new InvalidOperationException("Picker has no AppKit handler.");
        var contentView = native.Window?.ContentView
            ?? throw new InvalidOperationException("Picker is not attached to the real AppKit window.");
        evidence.Capture(contentView, "initial.png");

        if (picker.SelectedIndex == -1 && picker.Title == null &&
            native.IndexOfSelectedItem == 0 && native.Title == "January")
        {
            Record("initial", picker, native, evidence);
            evidence.Assert(picker.SelectedIndex == -1 && picker.Title == null &&
                picker.SelectedItem == null && native.IndexOfSelectedItem == 0 &&
                native.Title == "January" && native.SelectedItem?.Title == "January",
                "Original unselected Picker displays the first native item.");
            evidence.BaselineFailure("picker.unselected-first-item",
                "BASELINE_563: unselected untitled Picker displays January.");
            return Task.CompletedTask;
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
        evidence.Capture(contentView, "final.png");
        Console.WriteLine("PASS: all native AppKit Picker regression scenarios.");
        return Task.CompletedTask;
    }

    static void Activate(NSPopUpButton native, int index)
    {
        native.SelectItem(index);
        var action = native.Action
            ?? throw new InvalidOperationException("The Picker has no native activation action.");
        if (!NSApplication.SharedApplication.SendAction(action, native.Target, native))
            throw new InvalidOperationException("AppKit did not dispatch the Picker activation action.");
    }

    static void Check(string scenario, int selected, int nativeSelected, string title,
        Microsoft.Maui.Controls.Picker picker, NSPopUpButton native, RuntimeTestContext evidence)
    {
        Record(scenario, picker, native, evidence);
        evidence.Assert(picker.SelectedIndex == selected && native.IndexOfSelectedItem == nativeSelected &&
            native.Title == title && (nativeSelected != -1 || native.SelectedItem == null),
                $"{scenario}: expected managed={selected}, native={nativeSelected}, title='{title}'; " +
                $"actual managed={picker.SelectedIndex}, native={native.IndexOfSelectedItem}, " +
                $"title='{native.Title}', native item='{native.SelectedItem?.Title ?? "<null>"}'.",
                $"picker.{scenario}");
        if (selected == -1)
            evidence.Assert(picker.SelectedItem == null, $"{scenario}: unselected Picker has no managed SelectedItem.",
                $"picker.{scenario}.managed-item");
        evidence.Pass(scenario);
    }

    static void Record(string scenario, Microsoft.Maui.Controls.Picker picker, NSPopUpButton native, RuntimeTestContext evidence) =>
        evidence.AppendJson("states.jsonl", new
        {
            scenario,
            managedIndex = picker.SelectedIndex,
            managedItem = picker.SelectedItem,
            nativeIndex = (int)native.IndexOfSelectedItem,
            nativeTitle = native.Title,
            nativeItem = native.SelectedItem?.Title,
            pickerTitle = picker.Title
        });

    public override Window CreateWindow(IActivationState? activationState)
    {
        _month = new Microsoft.Maui.Controls.Picker
        {
            ItemsSource = new[] { "January", "February", "March" },
            AutomationId = "UnselectedMonth"
        };

        return new(new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                Children =
                {
                    new Label { Text = "Month (initially no selection and no title)" },
                    _month
                }
            }
        })
        {
            Title = "AppKit Picker regression",
            Width = 480,
            Height = 240
        };
    }
}
