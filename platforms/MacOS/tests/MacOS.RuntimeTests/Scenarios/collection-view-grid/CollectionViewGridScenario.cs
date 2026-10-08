using System.Runtime.CompilerServices;
using AppKit;
using CoreGraphics;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace MacOS.RuntimeTests.Scenarios.CollectionViewGrid;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("collection-view-grid", 4,
            context => new CollectionViewGridScenario().CreateDelegate(context),
            ExpectedAssertions: 8));
}

sealed class CollectionViewGridScenario : MauiRuntimeScenario
{
    const double Spacing = 12;
    readonly Card[] _cards =
    [
        new("First"),
        new("Second"),
        new("Third"),
        new("Fourth"),
    ];

    CollectionView _collectionView = null!;
    GridItemsLayout _layout = null!;

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var failures = new List<string>();

        await FlushLayoutAsync(_collectionView);
        var initialSnapshot = Snapshot(_collectionView);

        _layout.Span = 3;
        await FlushLayoutAsync(_collectionView);
        var tallSnapshot = Snapshot(_collectionView);
        evidence.WriteJson("tall-content.json", tallSnapshot);
        evidence.Capture(CaptureView(_collectionView), "tall-content.png");
        var tallMeasured = tallSnapshot.ItemFrames.All(frame => frame.Height > 150);
        var tallRowsOrdered = RowsAreOrdered(tallSnapshot.ItemFrames, 3);
        if (!tallMeasured) failures.Add("tall-content-measurement");
        if (!tallRowsOrdered) failures.Add("tall-content-overlap");

        evidence.WriteJson("span-change.json", new { initial = initialSnapshot, updated = tallSnapshot });
        evidence.Capture(CaptureView(_collectionView), "span-change.png");
        var spanApplied = HasThreeColumns(tallSnapshot.ItemFrames);
        var spanRowsOrdered = RowsAreOrdered(tallSnapshot.ItemFrames, 3);
        if (!spanApplied) failures.Add("span-change-layout");
        if (!spanRowsOrdered) failures.Add("span-change-overlap");

        var wideSnapshot = tallSnapshot;
        var nativeWindow = (_collectionView.Handler?.PlatformView is NSView view ? view.Window : null)
            ?? throw new InvalidOperationException("CollectionView is not attached to an AppKit window.");
        var frame = nativeWindow.Frame;
        nativeWindow.SetFrame(new CGRect(frame.X, frame.Y, 420, frame.Height), true);
        await FlushLayoutAsync(_collectionView);
        var narrowSnapshot = Snapshot(_collectionView);
        evidence.WriteJson("resize.json", new { wide = wideSnapshot, narrow = narrowSnapshot });
        var resizeHasItems = wideSnapshot.ItemFrames.Length > 0 && narrowSnapshot.ItemFrames.Length > 0;
        var resizeWidthApplied = resizeHasItems &&
            narrowSnapshot.ItemFrames[0].Width < wideSnapshot.ItemFrames[0].Width - 50;
        var resizeRemeasured = resizeHasItems &&
            narrowSnapshot.ItemFrames[0].Height > wideSnapshot.ItemFrames[0].Height + 20;
        if (!resizeWidthApplied) failures.Add("resize-width");
        if (!resizeRemeasured) failures.Add("resize-measurement");

        nativeWindow.SetFrame(new CGRect(frame.X, frame.Y, frame.Width, frame.Height), true);
        await FlushLayoutAsync(_collectionView);
        _collectionView.Header = "Workspace header";
        _collectionView.HeaderTemplate = new DataTemplate(() =>
            new Label { Text = "Workspace header", FontSize = 20, Padding = 8 });
        await FlushLayoutAsync(_collectionView);
        var headerSnapshot = Snapshot(_collectionView, hasHeader: true);
        evidence.WriteJson("header.json", headerSnapshot);
        evidence.Capture(CaptureView(_collectionView), "header.png");
        var headerSpans = headerSnapshot.HeaderFrame is { } header &&
            header.Width >= headerSnapshot.ContainerWidth - 1;
        var headerKeepsGrid = HasThreeColumns(headerSnapshot.ItemFrames) &&
            SameRow(headerSnapshot.ItemFrames.Take(3));
        if (!headerSpans) failures.Add("header-width");
        if (!headerKeepsGrid) failures.Add("header-grid-placement");

        if (failures.Count > 0)
        {
            evidence.Assert(!tallMeasured && tallSnapshot.ItemFrames.All(frame => Math.Abs(frame.Height - 44) < 1),
                "Baseline leaves composed Border cards at the 44-point estimate.",
                "collection-view-grid.baseline-tall-content");
            evidence.Assert(!spanApplied,
                "Baseline ignores an in-place GridItemsLayout Span change.",
                "collection-view-grid.baseline-span-change");
            evidence.Assert(!resizeRemeasured,
                "Baseline keeps stale item measurements after a cross-axis resize.",
                "collection-view-grid.baseline-resize");
            evidence.Assert(!headerKeepsGrid,
                "Baseline counts the spanning header as a grid cell.",
                "collection-view-grid.baseline-header");
            evidence.BaselineFailure("collection-view-grid.runtime-layout",
                "BASELINE_598: tall cards, live Span changes, resize remeasurement, and spanning headers are incorrect.");
            return;
        }

        evidence.Assert(tallMeasured, "Composed Border cards propagate their full native height.");
        evidence.Assert(tallRowsOrdered, "Tall vertical-grid rows are ordered with configured spacing.");
        evidence.Pass("Tall composed cards measure and do not overlap");

        evidence.Assert(spanApplied, "Changing Span from 1 to 3 creates three native columns.");
        evidence.Assert(spanRowsOrdered, "Rows remain ordered after the live Span change.");
        evidence.Pass("Live Span changes remeasure and reposition existing items");

        evidence.Assert(resizeWidthApplied, "Native grid cells follow the narrower window width.");
        evidence.Assert(resizeRemeasured, "Narrower grid cells are remeasured for wrapped content.");
        evidence.Pass("Cross-axis resize invalidates cached item measurements");

        evidence.Assert(headerSpans, "CollectionView header spans the full grid width.");
        evidence.Assert(headerKeepsGrid, "The first three items remain one row after the spanning header.");
        evidence.Pass("Spanning header does not consume a grid cell");
    }

    static async Task FlushLayoutAsync(CollectionView collectionView)
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        await RuntimeTestContext.FlushMainQueueAsync();
        var scrollView = Native(collectionView);
        scrollView.LayoutSubtreeIfNeeded();
        scrollView.Window?.ContentView?.LayoutSubtreeIfNeeded();
    }

    static Border CreateCard()
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        for (var i = 0; i < 9; i++)
        {
            content.Children.Add(new Label
            {
                Text = $"Workspace detail {i + 1}: a deliberately long value that wraps as the grid narrows",
                LineBreakMode = LineBreakMode.WordWrap,
            });
        }
        content.Children.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Button { Text = "Open" },
                new Button { Text = "Details" },
            }
        });

        return new Border
        {
            Padding = 12,
            StrokeThickness = 1,
            Content = content,
        };
    }

    static GridItemsLayout GridLayout(int span) =>
        new(span, ItemsLayoutOrientation.Vertical)
        {
            HorizontalItemSpacing = Spacing,
            VerticalItemSpacing = Spacing,
        };

    static GridSnapshot Snapshot(CollectionView collectionView, bool hasHeader = false)
    {
        var scrollView = Native(collectionView);
        var documentView = DocumentView(collectionView);
        var container = documentView.Subviews.SingleOrDefault()
            ?? throw new InvalidOperationException("CollectionView has no native items container.");
        var frames = container.Subviews.Select(view => FrameSnapshot.From(view.Frame)).ToArray();
        var itemFrames = hasHeader ? frames.Skip(1).ToArray() : frames;
        return new GridSnapshot(
            (double)container.Frame.Width,
            hasHeader ? frames[0] : null,
            itemFrames,
            FrameSnapshot.From(documentView.Frame),
            FrameSnapshot.From(scrollView.Frame));
    }

    static bool HasThreeColumns(IReadOnlyList<FrameSnapshot> frames) =>
        frames.Take(3).Select(frame => Math.Round(frame.X, 1)).Distinct().Count() == 3 &&
        SameRow(frames.Take(3));

    static bool SameRow(IEnumerable<FrameSnapshot> frames) =>
        frames.Select(frame => Math.Round(frame.Y, 1)).Distinct().Count() == 1;

    static bool RowsAreOrdered(IReadOnlyList<FrameSnapshot> frames, int span)
    {
        if (frames.Count <= span)
            return true;
        var firstRowBottom = frames.Take(span).Max(frame => frame.Y + frame.Height);
        return frames[span].Y >= firstRowBottom + Spacing - 0.5;
    }

    static NSScrollView Native(CollectionView collectionView) =>
        (collectionView.Handler as CollectionViewHandler)?.PlatformView
            ?? throw new InvalidOperationException("CollectionView has no AppKit handler.");

    static NSView DocumentView(CollectionView collectionView) =>
        Native(collectionView).DocumentView
            ?? throw new InvalidOperationException("CollectionView has no native document view.");

    static NSView CaptureView(CollectionView collectionView) =>
        Native(collectionView).Window?.ContentView
            ?? throw new InvalidOperationException("CollectionView is not attached to a real AppKit window.");

    public override Window CreateWindow(IActivationState? activationState)
    {
        _layout = GridLayout(1);
        _collectionView = new CollectionView
        {
            ItemsSource = _cards,
            ItemsLayout = _layout,
            ItemTemplate = new DataTemplate(CreateCard),
        };

        return new(new ContentPage
        {
            Padding = 24,
            Content = _collectionView,
        })
        {
            Title = "AppKit CollectionView grid regression",
            Width = 720,
            Height = 760,
        };
    }

    sealed record Card(string Name);
    sealed record GridSnapshot(
        double ContainerWidth,
        FrameSnapshot? HeaderFrame,
        FrameSnapshot[] ItemFrames,
        FrameSnapshot DocumentFrame,
        FrameSnapshot ScrollFrame);
    sealed record FrameSnapshot(double X, double Y, double Width, double Height)
    {
        public static FrameSnapshot From(CGRect frame) =>
            new(frame.X, frame.Y, frame.Width, frame.Height);
    }
}
