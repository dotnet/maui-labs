using System.Collections.Generic;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders an <see cref="OrderSummaryBlock"/> as XAML (see OrderSummaryView.xaml): a receipt-style card
/// with the order id, placed date, line items, and total — or a friendly "not found" / "looking up" state.
/// <para>
/// The visual tree lives entirely in XAML; this code-behind only maps the block onto bindable properties
/// in <see cref="RefreshFromContentContext"/>. The generated
/// <see cref="Microsoft.Maui.AI.Chat.ToolBlockAttribute"/> handler
/// only populates the block; the view remains an ordinary reusable XAML template.
/// </para>
/// </summary>
public partial class OrderSummaryView : ContentContextView
{
    public static readonly BindableProperty OrderNumberProperty =
        BindableProperty.Create(nameof(OrderNumber), typeof(string), typeof(OrderSummaryView), string.Empty);

    public static readonly BindableProperty PlacedOnProperty =
        BindableProperty.Create(nameof(PlacedOn), typeof(string), typeof(OrderSummaryView), string.Empty);

    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IReadOnlyList<ListItem>), typeof(OrderSummaryView));

    public static readonly BindableProperty TotalProperty =
        BindableProperty.Create(nameof(Total), typeof(decimal), typeof(OrderSummaryView), 0m);

    public static readonly BindableProperty OrderIdProperty =
        BindableProperty.Create(nameof(OrderId), typeof(string), typeof(OrderSummaryView), string.Empty);

    public static readonly BindableProperty IsFoundProperty =
        BindableProperty.Create(nameof(IsFound), typeof(bool), typeof(OrderSummaryView));

    public static readonly BindableProperty IsNotFoundProperty =
        BindableProperty.Create(nameof(IsNotFound), typeof(bool), typeof(OrderSummaryView));

    public static readonly BindableProperty IsPendingProperty =
        BindableProperty.Create(nameof(IsPending), typeof(bool), typeof(OrderSummaryView));

    public OrderSummaryView()
    {
        InitializeComponent();
    }

    /// <summary>The resolved order id (shown in the card header).</summary>
    public string OrderNumber
    {
        get => (string)GetValue(OrderNumberProperty);
        set => SetValue(OrderNumberProperty, value);
    }

    /// <summary>The order's placed date, pre-formatted for display.</summary>
    public string PlacedOn
    {
        get => (string)GetValue(PlacedOnProperty);
        set => SetValue(PlacedOnProperty, value);
    }

    /// <summary>The order's line items, as a fresh list per refresh so the list re-binds.</summary>
    public IReadOnlyList<ListItem>? Items
    {
        get => (IReadOnlyList<ListItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public decimal Total
    {
        get => (decimal)GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    /// <summary>The id the model looked up (shown in the not-found state).</summary>
    public string OrderId
    {
        get => (string)GetValue(OrderIdProperty);
        set => SetValue(OrderIdProperty, value);
    }

    public bool IsFound
    {
        get => (bool)GetValue(IsFoundProperty);
        set => SetValue(IsFoundProperty, value);
    }

    public bool IsNotFound
    {
        get => (bool)GetValue(IsNotFoundProperty);
        set => SetValue(IsNotFoundProperty, value);
    }

    public bool IsPending
    {
        get => (bool)GetValue(IsPendingProperty);
        set => SetValue(IsPendingProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is not OrderSummaryBlock block)
        {
            IsFound = IsNotFound = IsPending = false;
            return;
        }

        OrderId = block.OrderId;

        if (block.Order is { } order)
        {
            OrderNumber = order.Id;
            PlacedOn = order.PlacedAt.ToString("MMM d, yyyy • h:mm tt");
            Items = [.. order.Items];
            Total = order.Total;
            IsFound = true;
            IsNotFound = IsPending = false;
        }
        else if (block.HasResult)
        {
            // The tool ran but returned null — no order matched the id.
            IsNotFound = true;
            IsFound = IsPending = false;
        }
        else
        {
            // The call was emitted but the result has not streamed back yet.
            IsPending = true;
            IsFound = IsNotFound = false;
        }
    }
}
