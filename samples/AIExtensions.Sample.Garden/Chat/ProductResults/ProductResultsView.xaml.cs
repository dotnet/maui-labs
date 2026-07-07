using System.Collections.Generic;
using AIExtensions.Sample.Garden.Models;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders a <see cref="ProductResultsBlock"/> as XAML (see ProductResultsView.xaml). Adapts to the
/// number of products discovered in the turn: exactly one becomes a single detail card, several become
/// a horizontal carousel, and none (a not-found lookup) shows a friendly empty state.
/// <para>
/// The visual tree lives entirely in XAML; this code-behind only maps the block onto bindable
/// properties in <see cref="RefreshFromContentContext"/>.
/// </para>
/// </summary>
public partial class ProductResultsView : ContentContextView
{
    public static readonly BindableProperty ProductsProperty =
        BindableProperty.Create(nameof(Products), typeof(IReadOnlyList<Product>), typeof(ProductResultsView));

    public static readonly BindableProperty SingleProductProperty =
        BindableProperty.Create(nameof(SingleProduct), typeof(Product), typeof(ProductResultsView));

    public static readonly BindableProperty IsEmptyProperty =
        BindableProperty.Create(nameof(IsEmpty), typeof(bool), typeof(ProductResultsView));

    public static readonly BindableProperty IsSingleProperty =
        BindableProperty.Create(nameof(IsSingle), typeof(bool), typeof(ProductResultsView));

    public static readonly BindableProperty IsCarouselProperty =
        BindableProperty.Create(nameof(IsCarousel), typeof(bool), typeof(ProductResultsView));

    public ProductResultsView()
    {
        InitializeComponent();
    }

    /// <summary>The products, as a fresh list per refresh so the carousel's ItemsSource re-binds.</summary>
    public IReadOnlyList<Product>? Products
    {
        get => (IReadOnlyList<Product>?)GetValue(ProductsProperty);
        set => SetValue(ProductsProperty, value);
    }

    /// <summary>The single product to show as a detail card (when exactly one was found).</summary>
    public Product? SingleProduct
    {
        get => (Product?)GetValue(SingleProductProperty);
        set => SetValue(SingleProductProperty, value);
    }

    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public bool IsSingle
    {
        get => (bool)GetValue(IsSingleProperty);
        set => SetValue(IsSingleProperty, value);
    }

    public bool IsCarousel
    {
        get => (bool)GetValue(IsCarouselProperty);
        set => SetValue(IsCarouselProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is not ProductResultsBlock block)
        {
            IsEmpty = IsSingle = IsCarousel = false;
            return;
        }

        var products = block.Products;

        // Assign a fresh list so the CollectionView's ItemsSource binding refreshes on each update.
        Products = [.. products];
        SingleProduct = products.Count == 1 ? products[0] : null;
        IsSingle = products.Count == 1;
        IsCarousel = products.Count > 1;
        IsEmpty = products.Count == 0 && block.AnyResultReceived;
    }
}
