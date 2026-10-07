using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace MacOS.RuntimeTests.Scenarios.CollectionViewGrid;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("collection-view-grid", 1,
            context => new CollectionViewGridScenario().CreateDelegate(context),
            ExpectedAssertions: 2));
}

sealed class CollectionViewGridScenario : MauiRuntimeScenario
{
    readonly Card[] _cards =
    [
        new("First", 1),
        new("Tallest", 8),
        new("Third", 4),
        new("Second row", 2),
    ];

    CollectionView _collectionView = null!;

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        await RuntimeTestContext.FlushMainQueueAsync();

        var scrollView = (_collectionView.Handler as CollectionViewHandler)?.PlatformView
            ?? throw new InvalidOperationException("CollectionView has no AppKit handler.");
        scrollView.LayoutSubtreeIfNeeded();
        var contentView = scrollView.Window?.ContentView
            ?? throw new InvalidOperationException("CollectionView is not attached to the real AppKit window.");
        contentView.LayoutSubtreeIfNeeded();

        var documentView = scrollView.DocumentView
            ?? throw new InvalidOperationException("CollectionView has no native document view.");
        var itemsContainer = documentView.Subviews.SingleOrDefault()
            ?? throw new InvalidOperationException("CollectionView has no native items container.");
        var items = itemsContainer.Subviews;
        evidence.Assert(items.Length == _cards.Length,
            $"Expected {_cards.Length} native grid items, got {items.Length}.",
            "collection-view-grid.item-count");

        var frames = items.Select(view => view.Frame).ToArray();
        var firstRowTop = frames.Min(frame => frame.Y);
        var firstRow = frames.Where(frame => Math.Abs(frame.Y - firstRowTop) < 0.5).ToArray();
        var firstRowBottom = firstRow.Max(frame => frame.Y + frame.Height);
        var secondRowTop = frames.Where(frame => frame.Y > firstRowTop + 0.5).Min(frame => frame.Y);
        var overlaps = secondRowTop < firstRowBottom;

        evidence.WriteJson("frames.json", new
        {
            firstRowBottom,
            secondRowTop,
            verticalSpacing = 12,
            overlaps,
            frames = frames.Select((frame, index) => new
            {
                index,
                card = _cards[index].Name,
                x = frame.X,
                y = frame.Y,
                width = frame.Width,
                height = frame.Height,
                bottom = frame.Y + frame.Height,
            })
        });
        evidence.Capture(documentView, "grid.png");

        if (overlaps)
        {
            evidence.Assert(firstRow.Length == 3 && secondRowTop > firstRowTop,
                "The baseline contains three first-row items and a distinct overlapping second row.",
                "collection-view-grid.baseline-shape");
            evidence.BaselineFailure("collection-view-grid.rows-overlap",
                "BASELINE_598: the second grid row starts before the tallest first-row item ends.");
            return;
        }

        evidence.Assert(secondRowTop >= firstRowBottom + 12,
            $"Expected second row Y >= {firstRowBottom + 12}, got {secondRowTop}.",
            "collection-view-grid.rows-overlap");
        evidence.Pass("Vertical grid rows do not overlap");
    }

    public override Window CreateWindow(IActivationState? activationState)
    {
        _collectionView = new CollectionView
        {
            ItemsSource = _cards,
            ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 12,
                VerticalItemSpacing = 12,
            },
            ItemTemplate = new CardTemplateSelector(),
        };

        return new(new ContentPage
        {
            Padding = 24,
            Content = _collectionView,
        })
        {
            Title = "AppKit CollectionView grid regression",
            Width = 720,
            Height = 420,
        };
    }

    sealed record Card(string Name, int Lines);

    sealed class CardTemplateSelector : DataTemplateSelector
    {
        readonly Dictionary<int, DataTemplate> _templates = [];

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            var card = (Card)item;
            if (_templates.TryGetValue(card.Lines, out var template))
                return template;

            template = new DataTemplate(() =>
            {
                var stack = new VerticalStackLayout
                {
                    Spacing = 0,
                    BackgroundColor = Colors.CornflowerBlue,
                };
                for (var line = 0; line < card.Lines; line++)
                {
                    stack.Children.Add(new Label
                    {
                        Text = $"{card.Name} line {line + 1}",
                        TextColor = Colors.White,
                    });
                }
                return stack;
            });
            _templates.Add(card.Lines, template);
            return template;
        }
    }
}
