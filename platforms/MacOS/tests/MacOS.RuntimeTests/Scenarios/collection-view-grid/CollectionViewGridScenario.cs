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

    ContentPage _page = null!;

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        var failures = new List<string>();

        var tallLayout = GridLayout(3);
        var tall = CreateCollection(tallLayout);
        await ShowAsync(tall);
        var tallSnapshot = Snapshot(tall);
        evidence.WriteJson("tall-content.json", tallSnapshot);
        evidence.Capture(DocumentView(tall), "tall-content.png");
        var tallMeasured = tallSnapshot.ItemFrames.All(frame => frame.Height > 150);
        var tallRowsOrdered = RowsAreOrdered(tallSnapshot.ItemFrames, 3);
        if (!tallMeasured) failures.Add("tall-content-measurement");
        if (!tallRowsOrdered) failures.Add("tall-content-overlap");

        var dynamicLayout = GridLayout(1);
        var dynamic = CreateCollection(dynamicLayout);
        await ShowAsync(dynamic);
        dynamicLayout.Span = 3;
        await FlushLayoutAsync(dynamic);
        var dynamicSnapshot = Snapshot(dynamic);
        evidence.WriteJson("span-change.json", dynamicSnapshot);
        evidence.Capture(DocumentView(dynamic), "span-change.png");
        var spanApplied = HasThreeColumns(dynamicSnapshot.ItemFrames);
        var spanRowsOrdered = RowsAreOrdered(dynamicSnapshot.ItemFrames, 3);
        if (!spanApplied) failures.Add("span-change-layout");
        if (!spanRowsOrdered) failures.Add("span-change-overlap");

        var resizeLayout = GridLayout(3);
        var resize = CreateCollection(resizeLayout, wrappingText: true);
        await ShowAsync(resize);
        var wideSnapshot = Snapshot(resize);
        var nativeWindow = (resize.Handler?.PlatformView is NSView view ? view.Window : null)
            ?? throw new InvalidOperationException("CollectionView is not attached to an AppKit window.");
        var frame = nativeWindow.Frame;
        nativeWindow.SetFrame(new CGRect(frame.X, frame.Y, 420, frame.Height), true);
        await FlushLayoutAsync(resize);
        var narrowSnapshot = Snapshot(resize);
        evidence.WriteJson("resize.json", new { wide = wideSnapshot, narrow = narrowSnapshot });
        var resizeWidthApplied = narrowSnapshot.ItemFrames[0].Width < wideSnapshot.ItemFrames[0].Width - 50;
        var resizeRemeasured = narrowSnapshot.ItemFrames[0].Height > wideSnapshot.ItemFrames[0].Height + 20;
        if (!resizeWidthApplied) failures.Add("resize-width");
        if (!resizeRemeasured) failures.Add("resize-measurement");

        nativeWindow.SetFrame(new CGRect(frame.X, frame.Y, frame.Width, frame.Height), true);
        var headerLayout = GridLayout(3);
        var withHeader = CreateCollection(headerLayout, header: "Workspace header");
        await ShowAsync(withHeader);
        var headerSnapshot = Snapshot(withHeader, hasHeader: true);
        evidence.WriteJson("header.json", headerSnapshot);
        evidence.Capture(DocumentView(withHeader), "header.png");
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

    async Task ShowAsync(CollectionView collectionView)
    {
        _page.Content = collectionView;
        await FlushLayoutAsync(collectionView);
    }

    static async Task FlushLayoutAsync(CollectionView collectionView)
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        await RuntimeTestContext.FlushMainQueueAsync();
        var scrollView = Native(collectionView);
        scrollView.LayoutSubtreeIfNeeded();
        scrollView.Window?.ContentView?.LayoutSubtreeIfNeeded();
    }

    CollectionView CreateCollection(GridItemsLayout layout, bool wrappingText = false, object? header = null) =>
        new()
        {
            ItemsSource = _cards,
            ItemsLayout = layout,
            Header = header,
            HeaderTemplate = header == null ? null : new DataTemplate(() =>
                new Label { Text = "Workspace header", FontSize = 20, Padding = 8 }),
            ItemTemplate = new DataTemplate(() => CreateCard(wrappingText)),
        };

    static Border CreateCard(bool wrappingText)
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        for (var i = 0; i < 9; i++)
        {
            content.Children.Add(new Label
            {
                Text = wrappingText
                    ? $"Workspace detail {i + 1}: a deliberately long value that wraps as the grid narrows"
                    : $"Workspace detail {i + 1}",
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

    public override Window CreateWindow(IActivationState? activationState)
    {
        _page = new ContentPage { Padding = 24 };
        return new(_page)
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
