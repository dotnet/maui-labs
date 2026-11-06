using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace MacOS.RuntimeTests.Scenarios.FocusState;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("focus-state", 5, context => new FocusStateScenario().CreateDelegate(context),
            ExpectedAssertions: 13));
}

/// <summary>
/// Nothing on the AppKit head set <see cref="IView.IsFocused"/>, so <c>Focused</c>/<c>Unfocused</c> never
/// fired, the Focused visual state never applied and <c>VisualElement.IsFocused</c> was always false - even
/// while the user typed into the field. <c>Unfocus()</c> was not mapped at all.
/// </summary>
sealed class FocusStateScenario : MauiRuntimeScenario
{
    Entry _first = null!;
    Entry _second = null!;
    Editor _editor = null!;
    Entry _redirectTarget = null!;
    bool _redirectOnUnfocus;
    readonly List<string> _events = [];

    void Track(VisualElement view, string name)
    {
        view.Focused += (_, _) => _events.Add($"{name}:focused");
        view.Unfocused += (_, _) => _events.Add($"{name}:unfocused");
    }

    static async Task SettleAsync()
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        await Task.Delay(50);
    }

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var nativeWindow = (window.Handler?.PlatformView as NSWindow)
            ?? throw new InvalidOperationException("Window has no NSWindow.");
        nativeWindow.MakeKeyAndOrderFront(null);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        await SettleAsync();

        var focusedFirst = _first.Focus();
        await SettleAsync();
        evidence.AppendJson("focus.jsonl", new { step = "first", focusedFirst, first = _first.IsFocused, events = _events.ToArray() });

        if (focusedFirst && !_first.IsFocused)
        {
            evidence.Assert(true, "Unfixed AppKit makes the field first responder but never reports IsFocused.");
            evidence.BaselineFailure("focus-state.never-focused",
                "BASELINE_FOCUS: Entry became first responder but IsFocused stayed false.");
            return;
        }

        evidence.Assert(focusedFirst && _first.IsFocused, "Focus() on an Entry sets IsFocused.", "focus-state.entry-focused");
        evidence.Assert(_events.SequenceEqual(["first:focused"]), $"Focused fires once; events: {string.Join(",", _events)}.", "focus-state.entry-focused-event");
        evidence.Pass("entry-focus");

        _events.Clear();
        _second.Focus();
        await SettleAsync();
        evidence.AppendJson("focus.jsonl", new { step = "second", first = _first.IsFocused, second = _second.IsFocused, events = _events.ToArray() });
        evidence.Assert(!_first.IsFocused && _second.IsFocused, "Moving focus clears the previous field and sets the new one.", "focus-state.move");
        evidence.Assert(_events.SequenceEqual(["first:unfocused", "second:focused"]),
            $"Unfocused then Focused fire in order; events: {string.Join(",", _events)}.", "focus-state.move-events");
        evidence.Pass("move-focus");

        _events.Clear();
        _editor.Focus();
        await SettleAsync();
        evidence.AppendJson("focus.jsonl", new { step = "editor", second = _second.IsFocused, editor = _editor.IsFocused, events = _events.ToArray() });
        evidence.Assert(_editor.IsFocused, "An Editor (NSTextView inside its scroll view) reports focus.", "focus-state.editor");
        evidence.Assert(!_second.IsFocused, "The Entry loses focus to the Editor.", "focus-state.editor-previous");
        evidence.Assert(_events.SequenceEqual(["second:unfocused", "editor:focused"]),
            $"Editor focus events; events: {string.Join(",", _events)}.", "focus-state.editor-events");
        evidence.Pass("editor-focus");

        _events.Clear();
        _editor.Unfocus();
        await SettleAsync();
        evidence.AppendJson("focus.jsonl", new { step = "unfocus", editor = _editor.IsFocused, responder = nativeWindow.FirstResponder?.GetType().Name, events = _events.ToArray() });
        evidence.Assert(!_editor.IsFocused, "Unfocus() resigns the Editor and clears IsFocused.", "focus-state.unfocus");
        evidence.Assert(nativeWindow.FirstResponder is not NSTextView, "The text view is no longer first responder.", "focus-state.unfocus-responder");
        evidence.Assert(_events.SequenceEqual(["editor:unfocused"]), $"Unfocused fires once; events: {string.Join(",", _events)}.", "focus-state.unfocus-event");
        evidence.Pass("unfocus");

        // An Unfocused handler that moves focus elsewhere: the redirect wins and only one view is focused.
        _first.Focus();
        await SettleAsync();
        _events.Clear();
        _redirectOnUnfocus = true;
        _second.Focus();
        await SettleAsync();
        _redirectOnUnfocus = false;
        evidence.AppendJson("focus.jsonl", new
        {
            step = "redirect",
            first = _first.IsFocused,
            second = _second.IsFocused,
            target = _redirectTarget.IsFocused,
            events = _events.ToArray()
        });
        evidence.Assert(_redirectTarget.IsFocused, "Focus redirected from an Unfocused handler lands on the redirect target.", "focus-state.redirect-target");
        evidence.Assert(!_first.IsFocused && !_second.IsFocused,
            $"No other view is left focused; first={_first.IsFocused}, second={_second.IsFocused}.", "focus-state.redirect-single");
        evidence.Assert(!_events.Contains("second:focused") || _events.LastIndexOf("second:unfocused") > _events.LastIndexOf("second:focused"),
            $"The superseded view does not end up focused; events: {string.Join(",", _events)}.", "focus-state.redirect-events");
        evidence.Pass("unfocused-handler-redirect");
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        _first = new Entry { AutomationId = "FirstEntry", Placeholder = "First" };
        _second = new Entry { AutomationId = "SecondEntry", Placeholder = "Second" };
        _editor = new Editor { AutomationId = "Editor", HeightRequest = 80 };
        Track(_first, "first");
        Track(_second, "second");
        Track(_editor, "editor");
        _redirectTarget = new Entry { AutomationId = "RedirectTarget", Placeholder = "Redirect target" };
        Track(_redirectTarget, "target");
        _first.Unfocused += (_, _) =>
        {
            if (_redirectOnUnfocus)
                _redirectTarget.Focus();
        };

        return new(new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 8,
                Children = { _first, _second, _editor, _redirectTarget }
            }
        })
        {
            Title = "AppKit focus state regression",
            Width = 420,
            Height = 300
        };
    }
}
