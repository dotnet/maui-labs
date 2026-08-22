using System.Globalization;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.Layouts;

namespace AIExtensions.Sample.Garden.Components;

public sealed class WelcomeHero : ProductComponentView
{
    public WelcomeHero()
    {
        var title = new Label
        {
            Text = "Grow something good",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.PrimaryText,
        };
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
        Content = GardenComponentVisuals.Card(
            "WelcomeHero",
            new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    title,
                    new Label
                    {
                        Text = "Tell Sage what you want to grow and your space, and your shop will adapt around the goal.",
                        FontSize = 16,
                        TextColor = GardenComponentVisuals.SecondaryText,
                    },
                },
            });
    }
}

public sealed class SeasonalGardenTip : ProductComponentView
{
    public SeasonalGardenTip()
    {
        Content = GardenComponentVisuals.Card(
            "SeasonalGardenTip",
            new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("SeasonalGardenTipTitle", "Seasonal tip"),
                    new Label
                    {
                        Text = "Start small, group plants with similar light and water needs, and check soil moisture before watering.",
                        TextColor = GardenComponentVisuals.SecondaryText,
                    },
                },
            });
    }
}

public sealed class QuickActions : ProductComponentView
{
    public QuickActions(IGardenComponentActions actions)
    {
        var catalog = ActionButton("Browse products", () => actions.NavigateAsync("catalog"));
        var cart = ActionButton("View cart", () => actions.NavigateAsync("cart"));
        var orders = ActionButton("Past orders", () => actions.NavigateAsync("orders"));
        Content = GardenComponentVisuals.Card(
            "QuickActions",
            new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("QuickActionsTitle", "Quick actions"),
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { catalog, cart, orders },
                    },
                },
            });
    }

    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = GardenComponentVisuals.Primary,
            TextColor = Colors.White,
        };
        button.Clicked += async (_, _) => await action();
        return button;
    }
}

public sealed class CartSummary : ProductComponentView
{
    public CartSummary()
    {
        var total = new Label
        {
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.Primary,
        };
        total.SetBinding(Label.TextProperty, Bind("total", CurrencyValueConverter.Instance));
        Content = GardenComponentVisuals.Card(
            "CartSummary",
            new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("CartSummaryTitle", "Current cart"),
                    total,
                },
            });
    }
}

public sealed class RecentOrdersSummary : ProductComponentView
{
    public RecentOrdersSummary()
    {
        Content = new GardenOrderCollection(
            "RecentOrdersSummary",
            "Recent orders",
            compact: true);
    }
}

public sealed class RecommendationBundle : ProductComponentView
{
    public RecommendationBundle(IGardenComponentActions actions)
    {
        var title = GardenComponentVisuals.SectionTitle("RecommendationBundleTitle", "Recommended bundle");
        title.SetBinding(Label.TextProperty, Bind("title"));
        var reason = new Label { TextColor = GardenComponentVisuals.SecondaryText };
        reason.SetBinding(Label.TextProperty, Bind("reason"));
        var products = GardenProductCollection.Create(actions, "products", compact: false, showAdd: true);
        Content = GardenComponentVisuals.Card(
            "RecommendationBundle",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children = { title, reason, products },
            });
    }
}

public sealed class CatalogGrid(IGardenComponentActions actions)
    : GardenProductCollectionComponent(actions, "CatalogGrid", "Products", compact: false, showAdd: true);

public sealed class CatalogList(IGardenComponentActions actions)
    : GardenProductCollectionComponent(actions, "CatalogList", "Product list", compact: true, showAdd: true);

public sealed class CategoryShelves(IGardenComponentActions actions)
    : GardenProductCollectionComponent(actions, "CategoryShelves", "Browse by category", compact: false, showAdd: true);

public sealed class ComparisonTray(IGardenComponentActions actions)
    : GardenProductCollectionComponent(actions, "ComparisonTray", "Compare options", compact: true, showAdd: true);

public sealed class CatalogEmptyState : ProductComponentView
{
    public CatalogEmptyState()
    {
        Content = GardenComponentVisuals.Card(
            "CatalogEmptyState",
            new Label
            {
                Text = "No products match these filters.",
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = GardenComponentVisuals.SecondaryText,
            });
    }
}

public sealed class RecommendationStrip : ProductComponentView
{
    public RecommendationStrip(IGardenComponentActions actions)
    {
        Content = GardenComponentVisuals.Card(
            "RecommendationStrip",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("RecommendationStripTitle", "Recommended for your goal"),
                    GardenProductCollection.Create(actions, "products", compact: true, showAdd: true),
                },
            });
    }
}

public sealed class CartItems(IGardenComponentActions actions)
    : GardenCartCollectionComponent(actions, "CartItems", compact: false);

public sealed class CompactCartItems(IGardenComponentActions actions)
    : GardenCartCollectionComponent(actions, "CompactCartItems", compact: true);

public sealed class CartTotalsBreakdown : ProductComponentView
{
    public CartTotalsBreakdown()
    {
        var total = new Label
        {
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.Primary,
        };
        total.SetBinding(Label.TextProperty, Bind("total", CurrencyValueConverter.Instance));
        Content = GardenComponentVisuals.Card(
            "CartTotalsBreakdown",
            new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("CartTotalsTitle", "Order total"),
                    total,
                    new Label
                    {
                        Text = "Taxes and delivery are calculated at checkout.",
                        TextColor = GardenComponentVisuals.SecondaryText,
                    },
                },
            });
    }
}

public sealed class BudgetSummary : ProductComponentView
{
    public BudgetSummary()
    {
        var total = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.Accent,
        };
        total.SetBinding(Label.TextProperty, Bind("total", CurrencyValueConverter.Instance));
        Content = GardenComponentVisuals.Card(
            "BudgetSummary",
            new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("BudgetSummaryTitle", "Budget check"),
                    total,
                    new Label
                    {
                        Text = "Compare this total with the budget you shared in chat.",
                        TextColor = GardenComponentVisuals.SecondaryText,
                    },
                },
            });
    }
}

public sealed class SuggestedAddOns(IGardenComponentActions actions)
    : GardenProductCollectionComponent(actions, "SuggestedAddOns", "Suggested add-ons", compact: true, showAdd: true);

public sealed class CartEmptyState : ProductComponentView
{
    public CartEmptyState()
    {
        Content = GardenComponentVisuals.Card(
            "CartEmptyState",
            new Label
            {
                Text = "Your cart is empty. Browse products or ask Sage for a starter bundle.",
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = GardenComponentVisuals.SecondaryText,
            });
    }
}

public sealed class OrdersList(IGardenComponentActions actions)
    : GardenOrderCollectionComponent(actions, "OrdersList", "Order history", compact: false);

public sealed class OrderTimeline(IGardenComponentActions actions)
    : GardenOrderCollectionComponent(actions, "OrderTimeline", "Order timeline", compact: false);

public sealed class OrderSummary(IGardenComponentActions actions)
    : GardenOrderCollectionComponent(actions, "OrderSummary", "Matching orders", compact: true);

public sealed class OrderDetail(IGardenComponentActions actions)
    : GardenOrderCollectionComponent(actions, "OrderDetail", "Order details", compact: false);

public sealed class OrderStats : ProductComponentView
{
    public OrderStats()
    {
        var list = new HorizontalStackLayout
        {
            Spacing = 8,
        };
        BindableLayout.SetItemTemplate(
            list,
            new DataTemplate(() =>
            {
                var total = new Label
                {
                    FontAttributes = FontAttributes.Bold,
                    TextColor = GardenComponentVisuals.Primary,
                };
                total.SetBinding(Label.TextProperty, Bind("total", CurrencyValueConverter.Instance));
                return GardenComponentVisuals.Card("OrderStat", total, 10);
            }));
        list.SetBinding(BindableLayout.ItemsSourceProperty, nameof(UiObject.Children));
        Content = GardenComponentVisuals.Card(
            "OrderStats",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("OrderStatsTitle", "Spending summary"),
                    list,
                },
            });
    }
}

public sealed class OrdersEmptyState : ProductComponentView
{
    public OrdersEmptyState()
    {
        Content = GardenComponentVisuals.Card(
            "OrdersEmptyState",
            new Label
            {
                Text = "No past orders yet.",
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = GardenComponentVisuals.SecondaryText,
            });
    }
}

public sealed class ReviewSummary : ProductComponentView
{
    public ReviewSummary()
    {
        var reviews = new HorizontalStackLayout
        {
            Spacing = 6,
        };
        BindableLayout.SetItemTemplate(
            reviews,
            new DataTemplate(() =>
            {
                var rating = new Label
                {
                    TextColor = GardenComponentVisuals.Accent,
                    FontAttributes = FontAttributes.Bold,
                };
                rating.SetBinding(Label.TextProperty, Bind("rating", SuffixValueConverter.Instance, " ★"));
                return GardenComponentVisuals.Card("ReviewRating", rating, 8);
            }));
        reviews.SetBinding(BindableLayout.ItemsSourceProperty, nameof(UiObject.Children));
        Content = GardenComponentVisuals.Card(
            "ReviewSummary",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("ReviewSummaryTitle", "Review snapshot"),
                    reviews,
                },
            });
    }
}

public sealed class ReviewList : ProductComponentView
{
    public ReviewList()
    {
        var reviews = new VerticalStackLayout
        {
            Spacing = 8,
        };
        BindableLayout.SetItemTemplate(
            reviews,
            new DataTemplate(() =>
            {
                var rating = new Label
                {
                    FontAttributes = FontAttributes.Bold,
                    TextColor = GardenComponentVisuals.Accent,
                };
                rating.SetBinding(Label.TextProperty, Bind("rating", SuffixValueConverter.Instance, " ★"));
                var comment = new Label { TextColor = GardenComponentVisuals.SecondaryText };
                comment.SetBinding(Label.TextProperty, Bind("comment"));
                return GardenComponentVisuals.Card(
                    "ReviewItem",
                    new VerticalStackLayout { Spacing = 4, Children = { rating, comment } },
                    10);
            }));
        reviews.SetBinding(BindableLayout.ItemsSourceProperty, nameof(UiObject.Children));
        Content = GardenComponentVisuals.Card(
            "ReviewList",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("ReviewListTitle", "Customer reviews"),
                    reviews,
                },
            });
    }
}

public sealed class RelatedProducts(IGardenComponentActions actions)
    : GardenProductCollectionComponent(actions, "RelatedProducts", "Related products", compact: true, showAdd: true);

public sealed class StockAvailability : ProductComponentView
{
    public StockAvailability()
    {
        var stock = new Label
        {
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.Primary,
        };
        stock.SetBinding(Label.TextProperty, Bind("quantity", StockValueConverter.Instance));
        Content = GardenComponentVisuals.Card(
            "StockAvailability",
            new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    GardenComponentVisuals.SectionTitle("StockAvailabilityTitle", "Availability"),
                    stock,
                },
            });
    }
}

public abstract class GardenProductCollectionComponent : ProductComponentView
{
    protected GardenProductCollectionComponent(
        IGardenComponentActions actions,
        string automationId,
        string title,
        bool compact,
        bool showAdd)
    {
        Content = GardenComponentVisuals.Card(
            automationId,
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    GardenComponentVisuals.SectionTitle($"{automationId}Title", title),
                    GardenProductCollection.Create(actions, null, compact, showAdd),
                },
            });
    }
}

internal static class GardenProductCollection
{
    public static View Create(
        IGardenComponentActions actions,
        string? collectionPath,
        bool compact,
        bool showAdd)
    {
        Layout collection;
        if (compact)
        {
            collection = new VerticalStackLayout { Spacing = 6 };
        }
        else
        {
            collection = new FlexLayout
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                JustifyContent = FlexJustify.SpaceBetween,
            };
        }

        BindableLayout.SetItemTemplate(
            collection,
            new DataTemplate(() => CreateItem(actions, compact, showAdd)));
        collection.SetBinding(
            BindableLayout.ItemsSourceProperty,
            collectionPath is null
                ? new Microsoft.Maui.Controls.Binding(nameof(UiObject.Children))
                : new Microsoft.Maui.Controls.Binding($"[{collectionPath}].Children"));
        return collection;
    }

    private static View CreateItem(
        IGardenComponentActions actions,
        bool compact,
        bool showAdd)
    {
        var name = new Label
        {
            FontSize = compact ? 14 : 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.PrimaryText,
        };
        name.SetBinding(Label.TextProperty, ProductComponentView.Bind("name"));
        var price = new Label { TextColor = GardenComponentVisuals.SecondaryText };
        price.SetBinding(Label.TextProperty, ProductComponentView.Bind("price", CurrencyValueConverter.Instance));
        var details = new Button
        {
            Text = "Details",
            FontSize = 12,
            BackgroundColor = Colors.Transparent,
            TextColor = GardenComponentVisuals.Primary,
        };
        details.Clicked += async (sender, _) =>
        {
            if (sender is BindableObject bindable && TryValue(bindable, "sku", out var sku))
                await actions.OpenProductAsync(sku);
        };
        var actionsRow = new HorizontalStackLayout { Spacing = 6, Children = { details } };
        if (showAdd)
        {
            var add = new Button
            {
                Text = "Add",
                FontSize = 12,
                BackgroundColor = GardenComponentVisuals.Primary,
                TextColor = Colors.White,
            };
            add.Clicked += async (sender, _) =>
            {
                if (sender is BindableObject bindable && TryValue(bindable, "sku", out var sku))
                    await actions.AddToCartAsync(sku);
            };
            actionsRow.Children.Add(add);
        }

        var card = GardenComponentVisuals.Card(
            "AdaptiveProduct",
            new VerticalStackLayout
            {
                Spacing = 4,
                Children = { name, price, actionsRow },
            },
            compact ? 10 : 14);
        if (!compact)
            card.WidthRequest = 260;
        return card;
    }

    internal static bool TryValue(BindableObject sender, string name, out string value)
    {
        if (sender.BindingContext is UiObject item &&
            item[name].Value?.ToString() is { Length: > 0 } result)
        {
            value = result;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

public abstract class GardenCartCollectionComponent : ProductComponentView
{
    protected GardenCartCollectionComponent(
        IGardenComponentActions actions,
        string automationId,
        bool compact)
    {
        var collection = new VerticalStackLayout
        {
            Spacing = 6,
        };
        BindableLayout.SetItemTemplate(
            collection,
            new DataTemplate(() => CreateItem(actions, compact)));
        collection.SetBinding(
            BindableLayout.ItemsSourceProperty,
            new Microsoft.Maui.Controls.Binding("[items].Children"));
        Content = GardenComponentVisuals.Card(
            automationId,
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    GardenComponentVisuals.SectionTitle($"{automationId}Title", compact ? "Compact cart" : "Cart items"),
                    collection,
                },
            });
    }

    private static View CreateItem(IGardenComponentActions actions, bool compact)
    {
        var name = new Label
        {
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.PrimaryText,
        };
        name.SetBinding(Label.TextProperty, ProductComponentView.Bind("name"));
        var quantity = new Label { TextColor = GardenComponentVisuals.SecondaryText };
        quantity.SetBinding(Label.TextProperty, ProductComponentView.Bind("quantity", PrefixedValueConverter.Instance, "Qty "));
        var subtotal = new Label { TextColor = GardenComponentVisuals.Primary };
        subtotal.SetBinding(Label.TextProperty, ProductComponentView.Bind("subtotal", CurrencyValueConverter.Instance));
        var text = new VerticalStackLayout { Spacing = 2, Children = { name, quantity, subtotal } };

        var decrease = new Button { Text = "−", BackgroundColor = Colors.Transparent };
        decrease.Clicked += async (sender, _) => await ChangeQuantityAsync(actions, sender, -1);
        var increase = new Button { Text = "+", BackgroundColor = Colors.Transparent };
        increase.Clicked += async (sender, _) => await ChangeQuantityAsync(actions, sender, 1);
        var remove = new Button { Text = "Remove", BackgroundColor = Colors.Transparent, TextColor = Colors.DarkRed };
        remove.Clicked += async (sender, _) =>
        {
            if (sender is BindableObject bindable &&
                GardenProductCollection.TryValue(bindable, "sku", out var sku))
                await actions.RemoveFromCartAsync(sku);
        };
        var controls = new HorizontalStackLayout
        {
            Spacing = 2,
            Children = { decrease, increase, remove },
        };
        Grid.SetColumn(controls, 1);
        return GardenComponentVisuals.Card(
            "AdaptiveCartItem",
            new Grid
            {
                ColumnDefinitions =
                {
                    new(GridLength.Star),
                    new(GridLength.Auto),
                },
                Children =
                {
                    text,
                    controls,
                },
            },
            compact ? 8 : 10);
    }

    private static async Task ChangeQuantityAsync(
        IGardenComponentActions actions,
        object? sender,
        int delta)
    {
        if (sender is not BindableObject bindable ||
            bindable.BindingContext is not UiObject item ||
            !GardenProductCollection.TryValue(bindable, "sku", out var sku))
        {
            return;
        }

        var quantity = Convert.ToInt32(item["quantity"].Value, CultureInfo.InvariantCulture);
        await actions.SetCartQuantityAsync(sku, quantity + delta);
    }
}

public class GardenOrderCollectionComponent : ProductComponentView
{
    protected GardenOrderCollectionComponent(
        IGardenComponentActions actions,
        string automationId,
        string title,
        bool compact)
    {
        Content = new GardenOrderCollection(automationId, title, compact, actions);
    }
}

internal sealed class GardenOrderCollection : VerticalStackLayout
{
    public GardenOrderCollection(
        string automationId,
        string title,
        bool compact,
        IGardenComponentActions? actions = null)
    {
        Spacing = 8;
        Children.Add(GardenComponentVisuals.SectionTitle($"{automationId}Title", title));
        var collection = new VerticalStackLayout
        {
            Spacing = 6,
        };
        BindableLayout.SetItemTemplate(
            collection,
            new DataTemplate(() => CreateItem(actions, compact)));
        collection.SetBinding(BindableLayout.ItemsSourceProperty, nameof(UiObject.Children));
        Children.Add(collection);
    }

    private static View CreateItem(IGardenComponentActions? actions, bool compact)
    {
        var date = new Label
        {
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.PrimaryText,
        };
        date.SetBinding(Label.TextProperty, ProductComponentView.Bind("placedAt"));
        var total = new Label { TextColor = GardenComponentVisuals.Primary };
        total.SetBinding(Label.TextProperty, ProductComponentView.Bind("total", CurrencyValueConverter.Instance));
        var content = new VerticalStackLayout { Spacing = 3, Children = { date, total } };
        if (actions is not null)
        {
            var open = new Button
            {
                Text = "Open",
                BackgroundColor = Colors.Transparent,
                TextColor = GardenComponentVisuals.Primary,
            };
            open.Clicked += async (sender, _) =>
            {
                if (sender is BindableObject bindable &&
                    GardenProductCollection.TryValue(bindable, "id", out var id))
                    await actions.OpenOrderAsync(id);
            };
            var reorder = new Button
            {
                Text = "Reorder",
                BackgroundColor = GardenComponentVisuals.Primary,
                TextColor = Colors.White,
            };
            reorder.Clicked += async (sender, _) =>
            {
                if (sender is BindableObject bindable &&
                    GardenProductCollection.TryValue(bindable, "id", out var id))
                    await actions.ReorderAsync(id);
            };
            content.Children.Add(new HorizontalStackLayout
            {
                Spacing = 6,
                Children = { open, reorder },
            });
        }

        return GardenComponentVisuals.Card("AdaptiveOrder", content, compact ? 8 : 12);
    }
}

internal sealed class SuffixValueConverter : IValueConverter
{
    public static SuffixValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => $"{value}{parameter}";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
