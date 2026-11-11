using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MacOS.RuntimeTests.Scenarios.ScrollViewMeasure;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("scrollview-measure", 6, context => new ScrollViewMeasureScenario().CreateDelegate(context),
            ExpectedAssertions: 14));
}

/// <summary>
/// A ScrollView sized by its parent - an Auto row, a stack - used to measure as ~0 because the handler
/// asked NSScrollView for a fitting size it doesn't have. A horizontal ScrollView in an Auto row
/// collapsed to nothing (e.g. a toolbar or ribbon body), and MaximumHeightRequest had nothing to clamp.
/// Content is measured inside the ScrollView's own width request (clamped by its maximum) and padding.
/// </summary>
sealed class ScrollViewMeasureScenario : MauiRuntimeScenario
{
    const double ItemHeight = 64;
    const double ItemWidth = 120;
    const int ItemCount = 12;

    ScrollView _horizontal = null!;
    ScrollView _capped = null!;
    ScrollView _fill = null!;
    View _horizontalContent = null!;
    ScrollView _wrapping = null!;
    Label _wrappingLabel = null!;
    ScrollView _overMaximum = null!;
    Label _overMaximumLabel = null!;
    ScrollView _padded = null!;
    View _paddedContent = null!;

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        // let the first layout pass (and any follow-up invalidation) settle
        for (var i = 0; i < 3; i++)
        {
            await RuntimeTestContext.FlushMainQueueAsync();
            await Task.Delay(100);
        }

        var native = _horizontal.Handler?.PlatformView as NSScrollView
            ?? throw new InvalidOperationException("Horizontal ScrollView has no AppKit platform view.");
        evidence.WriteJson("sizes.json", new
        {
            horizontal = new { _horizontal.Width, _horizontal.Height, native = native.Frame.Height },
            capped = new { _capped.Width, _capped.Height },
            fill = new { _fill.Width, _fill.Height },
            wrapping = new { _wrapping.Width, _wrapping.Height, label = new { _wrappingLabel.Width, _wrappingLabel.Height } },
            overMaximum = new { _overMaximum.Width, _overMaximum.Height, label = new { _overMaximumLabel.Width, _overMaximumLabel.Height } },
            padded = new { _padded.Height, content = new { _paddedContent.X, _paddedContent.Y, _paddedContent.Height } }
        });

        if (_horizontal.Height < 1)
        {
            evidence.Assert(true, "Unfixed AppKit measures a horizontal ScrollView in an Auto row as zero tall.");
            evidence.BaselineFailure("scrollview-measure.horizontal-collapsed",
                "BASELINE_SCROLL: horizontal ScrollView in an Auto row measured 0 tall.");
            return;
        }

        evidence.Assert(Math.Abs(_horizontal.Height - ItemHeight) < 1,
            $"Horizontal ScrollView takes its content's height; expected {ItemHeight}, actual {_horizontal.Height}.",
            "scrollview-measure.horizontal-height");
        evidence.Assert(Math.Abs(native.Frame.Height - ItemHeight) < 1,
            $"The NSScrollView frame matches; actual {native.Frame.Height}.",
            "scrollview-measure.horizontal-native-height");
        evidence.Assert(_horizontalContent.Width >= ItemWidth * ItemCount - 1,
            $"Content is arranged at its full scrollable width; actual {_horizontalContent.Width}.",
            "scrollview-measure.horizontal-content-width");
        evidence.Pass("horizontal-auto-row");

        evidence.Assert(Math.Abs(_capped.Height - 150) < 1,
            $"MaximumHeightRequest clamps a ScrollView measured from its content; actual {_capped.Height}.",
            "scrollview-measure.maximum-height");
        evidence.Assert(_capped.ContentSize.Height >= 399,
            $"The clamped ScrollView still scrolls its full content; content height {_capped.ContentSize.Height}.",
            "scrollview-measure.maximum-height-content");
        evidence.Pass("maximum-height");

        var available = window.Height - _fill.Y;
        evidence.Assert(_fill.Height > 100 && _fill.Height <= available + 1,
            $"A ScrollView in a star row fills the row and stays within it; actual {_fill.Height}, available {available}.",
            "scrollview-measure.fill-row");
        evidence.Assert(_fill.ContentSize.Height >= 1999,
            $"The star-row ScrollView scrolls its full content; content height {_fill.ContentSize.Height}.",
            "scrollview-measure.fill-row-content");
        evidence.Pass("fill-row");

        evidence.Assert(_wrappingLabel.Width <= 201,
            $"Content is measured inside the ScrollView's WidthRequest; label width {_wrappingLabel.Width}.",
            "scrollview-measure.wrapping-width");
        evidence.Assert(_wrapping.Height >= _wrappingLabel.Height - 1 && _wrappingLabel.Height > 20,
            $"The ScrollView is tall enough for the text wrapped at that width; scroller {_wrapping.Height}, label {_wrappingLabel.Height}.",
            "scrollview-measure.wrapping-height");
        evidence.Pass("wrapping-width");

        evidence.Assert(_overMaximum.Width <= 201,
            $"MaximumWidthRequest clamps a larger WidthRequest; scroller width {_overMaximum.Width}.",
            "scrollview-measure.over-maximum-width");
        evidence.Assert(_overMaximumLabel.Width <= 201,
            $"Content is measured at the clamped width, not the WidthRequest; label width {_overMaximumLabel.Width}.",
            "scrollview-measure.over-maximum-label-width");
        evidence.Assert(_overMaximum.Height >= _overMaximumLabel.Height - 1 && _overMaximumLabel.Height > 20,
            $"The ScrollView is tall enough for the text wrapped at the clamped width; scroller {_overMaximum.Height}, label {_overMaximumLabel.Height}.",
            "scrollview-measure.over-maximum-height");
        evidence.Pass("explicit-over-maximum");

        evidence.Assert(Math.Abs(_padded.Height - (ItemHeight + 20)) < 1,
            $"Padding is part of the measured size; expected {ItemHeight + 20}, actual {_padded.Height}.",
            "scrollview-measure.padding-height");
        evidence.Assert(Math.Abs(_paddedContent.X - 10) < 1 && Math.Abs(_paddedContent.Y - 10) < 1,
            $"Content is arranged inside the padding; content at {_paddedContent.X},{_paddedContent.Y}.",
            "scrollview-measure.padding-offset");
        evidence.Pass("padding");
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        // an explicit strip height keeps the expected value independent of child measurement
        var strip = new HorizontalStackLayout { Spacing = 0, HeightRequest = ItemHeight };
        for (var i = 0; i < ItemCount; i++)
        {
            strip.Children.Add(new BoxView
            {
                WidthRequest = ItemWidth,
                HeightRequest = ItemHeight,
                Color = i % 2 == 0 ? Colors.SteelBlue : Colors.LightSteelBlue
            });
        }
        _horizontalContent = strip;

        _horizontal = new ScrollView
        {
            AutomationId = "HorizontalScroll",
            Orientation = ScrollOrientation.Horizontal,
            Content = strip
        };

        _capped = new ScrollView
        {
            AutomationId = "CappedScroll",
            MaximumHeightRequest = 150,
            Content = new BoxView { HeightRequest = 400, Color = Colors.Orange }
        };

        _fill = new ScrollView
        {
            AutomationId = "FillScroll",
            Content = new BoxView { HeightRequest = 2000, Color = Colors.SeaGreen }
        };

        _wrappingLabel = new Label
        {
            Text = string.Join(" ", Enumerable.Repeat("Wrapping text measured at the scroller's own width.", 6)),
            LineBreakMode = LineBreakMode.WordWrap
        };
        _wrapping = new ScrollView
        {
            AutomationId = "WrappingScroll",
            WidthRequest = 200,
            HorizontalOptions = LayoutOptions.Start,
            Content = _wrappingLabel
        };

        _overMaximumLabel = new Label
        {
            Text = string.Join(" ", Enumerable.Repeat("Wrapping text measured at the maximum, not the request.", 6)),
            LineBreakMode = LineBreakMode.WordWrap
        };
        _overMaximum = new ScrollView
        {
            AutomationId = "OverMaximumScroll",
            WidthRequest = 400,
            MaximumWidthRequest = 200,
            HorizontalOptions = LayoutOptions.Start,
            Content = _overMaximumLabel
        };

        _paddedContent = new BoxView { WidthRequest = 900, HeightRequest = ItemHeight, Color = Colors.Plum };
        _padded = new ScrollView
        {
            AutomationId = "PaddedScroll",
            Orientation = ScrollOrientation.Horizontal,
            Padding = 10,
            Content = _paddedContent
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 8
        };
        grid.Add(_horizontal, 0, 0);
        grid.Add(_capped, 0, 1);
        grid.Add(_wrapping, 0, 2);
        grid.Add(_overMaximum, 0, 3);
        grid.Add(_padded, 0, 4);
        grid.Add(_fill, 0, 5);

        return new(new ContentPage { Content = grid })
        {
            Title = "AppKit ScrollView measure regression",
            Width = 600,
            Height = 900
        };
    }
}
