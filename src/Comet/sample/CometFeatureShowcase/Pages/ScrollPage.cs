using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiVerticalStackLayout = Microsoft.Maui.Controls.VerticalStackLayout;
using MauiSelectionMode = Microsoft.Maui.Controls.SelectionMode;
using MauiFontAttributes = Microsoft.Maui.Controls.FontAttributes;

namespace CometFeatureShowcase.Pages;

public class ScrollPageState { }

public class ScrollPage : Component<ScrollPageState>
{
    class ItemModel
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
    }

    ObservableCollection<ItemModel> items = new();
    int totalLoaded = 20;

    public override Comet.View Render()
    {
        // Use Grid with Auto header + * collection to properly constrain CollectionView height
        var root = new MauiGrid { BackgroundColor = Color.FromArgb("#F5F5F5") };
        root.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header
        var header = new MauiVerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(16, 16, 16, 12),
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        header.Add(new MauiLabel
        {
            Text = "Infinite Scroll Demo",
            TextColor = Colors.Black,
            FontSize = 24,
            FontAttributes = MauiFontAttributes.Bold,
        });

        header.Add(new MauiLabel
        {
            Text = "Scroll down to load more items (RemainingItemsThreshold = 5)",
            TextColor = Colors.DarkGray,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var counterLabel = new MauiLabel
        {
            Text = $"Loaded: {totalLoaded} items",
            TextColor = Colors.Gray,
            FontSize = 12,
        };

        header.Add(counterLabel);
        MauiGrid.SetRow(header, 0);
        root.Add(header);

        // CollectionView directly in Grid row (no ScrollView wrapper!)
        var collection = new MauiCollectionView
        {
            ItemsSource = items,
            RemainingItemsThreshold = 5,
            SelectionMode = MauiSelectionMode.Single,
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        collection.RemainingItemsThresholdReachedCommand = new Command(() =>
        {
            LoadMoreItems(counterLabel);
        });

        collection.ItemTemplate = new DataTemplate(() => BuildItemTemplate());

        MauiGrid.SetRow(collection, 1);
        root.Add(collection);

        InitializeItems();

        return new MauiViewHost(root);
    }

    void InitializeItems()
    {
        for (int i = 0; i < totalLoaded; i++)
        {
            items.Add(new ItemModel
            {
                Id = i + 1,
                Title = $"Item #{i + 1}",
                Description = $"Sample description for item {i + 1}"
            });
        }
    }

    void LoadMoreItems(MauiLabel counterLabel)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            int newCount = Math.Min(totalLoaded + 10, 100);

            // Must add items on UI thread for proper layout updates
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                for (int i = totalLoaded; i < newCount; i++)
                {
                    items.Add(new ItemModel
                    {
                        Id = i + 1,
                        Title = $"Item #{i + 1}",
                        Description = $"Sample description for item {i + 1}"
                    });
                }
                totalLoaded = newCount;
                counterLabel.Text = $"Loaded: {totalLoaded} items";
            });
        });
    }

    Microsoft.Maui.Controls.View BuildItemTemplate()
    {
        var card = new MauiBorder
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            BackgroundColor = Colors.White,
            StrokeThickness = 1,
            Padding = new Thickness(12),
            Margin = new Thickness(16, 6, 16, 6),
        };

        var stack = new MauiVerticalStackLayout { Spacing = 4 };

        var titleLabel = new MauiLabel
        {
            TextColor = Colors.Black,
            FontSize = 14,
            FontAttributes = MauiFontAttributes.Bold,
        };
        titleLabel.SetBinding(MauiLabel.TextProperty, "Title");

        var descLabel = new MauiLabel
        {
            TextColor = Colors.Gray,
            FontSize = 12,
        };
        descLabel.SetBinding(MauiLabel.TextProperty, "Description");

        stack.Add(titleLabel);
        stack.Add(descLabel);
        card.Content = stack;

        return card;
    }
}
