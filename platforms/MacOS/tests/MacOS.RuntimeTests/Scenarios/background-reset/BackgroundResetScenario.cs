using System.ComponentModel;
using System.Runtime.CompilerServices;
using AppKit;
using CoreAnimation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MacOS.RuntimeTests.Scenarios.BackgroundReset;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("background-reset", 4, context => new BackgroundResetScenario().CreateDelegate(context),
            ExpectedAssertions: 10));
}

/// <summary>
/// Clearing a background (setting it back to null) left the previous colour on the layer for layouts,
/// ContentViews, Pickers and ImageButtons. The common way to hit it is a DataTrigger: when its condition
/// stops matching, MAUI restores the property's default (null), and the triggered colour stayed - e.g. a
/// list's "selected row" highlight remained on every row that had ever been selected.
/// </summary>
sealed class BackgroundResetScenario : MauiRuntimeScenario
{
    sealed class RowState : INotifyPropertyChanged
    {
        bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    readonly RowState _row = new();
    Grid _direct = null!;
    Grid _triggered = null!;
    ContentView _content = null!;
    Picker _picker = null!;
    ImageButton _imageButton = null!;

    static CALayer? LayerOf(VisualElement view) => (view.Handler?.PlatformView as NSView)?.Layer;
    static bool HasColor(VisualElement view) => LayerOf(view)?.BackgroundColor is not null;

    static async Task SettleAsync()
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        await Task.Delay(50);
    }

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        await SettleAsync();
        var states = new List<object>();
        void Record(string step) => states.Add(new
        {
            step,
            direct = HasColor(_direct),
            triggered = HasColor(_triggered),
            content = HasColor(_content),
            picker = HasColor(_picker),
            imageButton = HasColor(_imageButton)
        });

        // layout: set, then clear
        _direct.BackgroundColor = Colors.Orange;
        await SettleAsync();
        Record("layout-set");
        var setOk = HasColor(_direct);
        _direct.BackgroundColor = null;
        await SettleAsync();
        Record("layout-cleared");

        if (setOk && HasColor(_direct))
        {
            evidence.WriteJson("states.json", states);
            evidence.Assert(true, "Unfixed AppKit keeps a layout's previous background after it is cleared.");
            evidence.BaselineFailure("background-reset.layout-kept-color",
                "BASELINE_BACKGROUND: clearing a Grid's BackgroundColor left the old colour on its layer.");
            return;
        }

        evidence.Assert(setOk, "Setting a Grid's BackgroundColor colours its layer.", "background-reset.layout-set");
        evidence.Assert(!HasColor(_direct), "Clearing a Grid's BackgroundColor clears its layer.", "background-reset.layout-cleared");
        evidence.Pass("layout-direct");

        // layout: DataTrigger enter, then exit (the trigger restores the default, null)
        _row.IsSelected = true;
        await SettleAsync();
        Record("trigger-on");
        evidence.Assert(HasColor(_triggered), "A DataTrigger's BackgroundColor setter colours the layer.", "background-reset.trigger-on");
        _row.IsSelected = false;
        await SettleAsync();
        Record("trigger-off");
        evidence.Assert(!HasColor(_triggered), "When the DataTrigger stops matching, the layer colour is removed.", "background-reset.trigger-off");
        evidence.Pass("layout-data-trigger");

        // ContentView
        _content.BackgroundColor = Colors.Purple;
        await SettleAsync();
        evidence.Assert(HasColor(_content), "Setting a ContentView's BackgroundColor colours its layer.", "background-reset.content-set");
        _content.BackgroundColor = null;
        await SettleAsync();
        Record("content-cleared");
        evidence.Assert(!HasColor(_content), "Clearing a ContentView's BackgroundColor clears its layer.", "background-reset.content-cleared");
        evidence.Pass("content-view");

        // native controls that only coloured, never cleared
        _picker.BackgroundColor = Colors.Teal;
        _imageButton.BackgroundColor = Colors.Teal;
        await SettleAsync();
        evidence.Assert(HasColor(_picker), "Setting a Picker's BackgroundColor colours its layer.", "background-reset.picker-set");
        evidence.Assert(HasColor(_imageButton), "Setting an ImageButton's BackgroundColor colours its layer.", "background-reset.imagebutton-set");
        _picker.BackgroundColor = null;
        _imageButton.BackgroundColor = null;
        await SettleAsync();
        Record("controls-cleared");
        evidence.Assert(!HasColor(_picker), "Clearing a Picker's BackgroundColor clears its layer.", "background-reset.picker-cleared");
        evidence.Assert(!HasColor(_imageButton), "Clearing an ImageButton's BackgroundColor clears its layer.", "background-reset.imagebutton-cleared");
        evidence.Pass("native-controls");

        evidence.WriteJson("states.json", states);
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        _direct = new Grid { AutomationId = "DirectGrid", HeightRequest = 40, Children = { new Label { Text = "Grid" } } };

        _triggered = new Grid
        {
            AutomationId = "TriggeredGrid",
            HeightRequest = 40,
            BindingContext = _row,
            Children = { new Label { Text = "DataTrigger row" } }
        };
        var trigger = new DataTrigger(typeof(Grid))
        {
            Binding = new Binding(nameof(RowState.IsSelected)),
            Value = true
        };
        trigger.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Colors.CornflowerBlue });
        _triggered.Triggers.Add(trigger);

        _content = new ContentView { AutomationId = "Content", HeightRequest = 40, Content = new Label { Text = "ContentView" } };
        _picker = new Picker { AutomationId = "Picker", ItemsSource = new[] { "One", "Two" } };
        _imageButton = new ImageButton { AutomationId = "ImageButton", WidthRequest = 40, HeightRequest = 40 };

        return new(new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 8,
                Children = { _direct, _triggered, _content, _picker, _imageButton }
            }
        })
        {
            Title = "AppKit background reset regression",
            Width = 420,
            Height = 360
        };
    }
}
