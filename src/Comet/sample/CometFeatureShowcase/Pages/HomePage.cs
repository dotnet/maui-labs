using Microsoft.Maui.Controls;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiBorder = Microsoft.Maui.Controls.Border;

namespace CometFeatureShowcase.Pages;

public class HomePageState { }

public class HomePage : Component<HomePageState>
{
    class ItemModel
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
    }

    ObservableCollection<ItemModel> items = new();

    public override Comet.View Render()
    {
        var root = new Microsoft.Maui.Controls.Grid { BackgroundColor = Color.FromArgb("#F5F5F5") };

        var content = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16),
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        // Title
        content.Add(new MauiLabel
        {
            Text = "BindableLayout Demo",
            TextColor = Colors.Black,
            FontSize = 24,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
            Margin = new Thickness(0, 8, 0, 0),
        });

        content.Add(new MauiLabel
        {
            Text = "Add or remove items from ObservableCollection",
            TextColor = Colors.DarkGray,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8),
        });

        // Buttons
        var buttonStack = new Microsoft.Maui.Controls.HorizontalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 12),
        };

        var addButton = new Microsoft.Maui.Controls.Button
        {
            Text = "Add Item",
            BackgroundColor = Color.FromArgb("#2196F3"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(16, 8),
        };
        addButton.Clicked += (s, e) => AddItem();
        buttonStack.Add(addButton);

        var removeButton = new Microsoft.Maui.Controls.Button
        {
            Text = "Remove Last",
            BackgroundColor = Color.FromArgb("#FF9800"),
            TextColor = Colors.White,
            CornerRadius = 6,
            Padding = new Thickness(16, 8),
        };
        removeButton.Clicked += (s, e) => RemoveItem();
        buttonStack.Add(removeButton);

        content.Add(buttonStack);

        // Items list
        var itemsList = new Microsoft.Maui.Controls.StackLayout();
        foreach (var item in items)
        {
            itemsList.Add(BuildItemView(item));
        }

        items.CollectionChanged += (s, e) =>
        {
            itemsList.Clear();
            foreach (var item in items)
            {
                itemsList.Add(BuildItemView(item));
            }
        };

        var scroll = new Microsoft.Maui.Controls.ScrollView
        {
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            Content = itemsList,
        };

        content.Add(scroll);
        root.Add(content);

        InitializeItems();

        return new MauiViewHost(root);
    }

    Microsoft.Maui.Controls.View BuildItemView(ItemModel item)
    {
        var itemBorder = new MauiBorder
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            BackgroundColor = Colors.White,
            StrokeThickness = 1,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 4),
        };

        var itemStack = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 4,
        };

        itemStack.Add(new MauiLabel
        {
            Text = item.Title ?? "Item",
            TextColor = Colors.Black,
            FontSize = 14,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
        });

        itemStack.Add(new MauiLabel
        {
            Text = item.Description ?? "No description",
            TextColor = Colors.Gray,
            FontSize = 12,
        });

        itemBorder.Content = itemStack;
        return itemBorder;
    }

    void InitializeItems()
    {
        items.Add(new ItemModel { Title = "Item 1", Description = "Click buttons to add/remove items" });
        items.Add(new ItemModel { Title = "Item 2", Description = "Updates happen instantly" });
        items.Add(new ItemModel { Title = "Item 3", Description = "This is BindableLayout in action!" });
    }

    void AddItem()
    {
        items.Add(new ItemModel 
        { 
            Title = $"Item {items.Count + 1}", 
            Description = $"Added at {DateTime.Now:HH:mm:ss}" 
        });
    }

    void RemoveItem()
    {
        if (items.Count > 0)
            items.RemoveAt(items.Count - 1);
    }
}
