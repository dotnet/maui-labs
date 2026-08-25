using AIExtensions.Sample.Garden.ViewModels;
using Microsoft.Maui.Graphics;

namespace AIExtensions.Sample.Garden.Views.Charts;

public sealed class SpendingByCategoryChartView : OrderBarChartView
{
    public SpendingByCategoryChartView()
        : base(
            "Where the money went",
            "#5B8C5A",
            value => value.ToString("C0"))
    {
    }
}

public sealed class PopularProductsChartView : OrderBarChartView
{
    public PopularProductsChartView()
        : base(
            "Most popular products",
            "#D8904F",
            value => $"{value:0} sold")
    {
    }
}

public abstract class OrderBarChartView : ContentView
{
    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(
        nameof(Items),
        typeof(IReadOnlyList<OrderInsightValue>),
        typeof(OrderBarChartView),
        defaultValue: Array.Empty<OrderInsightValue>(),
        propertyChanged: static (bindable, _, value) =>
            ((OrderBarChartView)bindable).SetItems(
                (IReadOnlyList<OrderInsightValue>?)value));

    private readonly OrderBarChartDrawable _drawable;
    private readonly GraphicsView _graphicsView;

    protected OrderBarChartView(
        string title,
        string accentColor,
        Func<decimal, string> formatValue)
    {
        WidthRequest = 280;
        HeightRequest = 205;
        HorizontalOptions = LayoutOptions.Start;

        _drawable = new OrderBarChartDrawable(
            title,
            Color.FromArgb(accentColor),
            formatValue);
        _graphicsView = new GraphicsView
        {
            Drawable = _drawable,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        Content = _graphicsView;
    }

    public IReadOnlyList<OrderInsightValue> Items
    {
        get => (IReadOnlyList<OrderInsightValue>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    private void SetItems(IReadOnlyList<OrderInsightValue>? items)
    {
        _drawable.Items = items ?? [];
        _graphicsView.Invalidate();
    }
}

internal sealed class OrderBarChartDrawable(
    string title,
    Color accentColor,
    Func<decimal, string> formatValue) : IDrawable
{
    public IReadOnlyList<OrderInsightValue> Items { get; set; } = [];

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Color.FromArgb("#FFFFFF");
        canvas.FillRoundedRectangle(
            dirtyRect.X + 1,
            dirtyRect.Y + 1,
            dirtyRect.Width - 2,
            dirtyRect.Height - 2,
            14);

        canvas.StrokeColor = Color.FromArgb("#D8E2D4");
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(
            dirtyRect.X + 1,
            dirtyRect.Y + 1,
            dirtyRect.Width - 2,
            dirtyRect.Height - 2,
            14);

        canvas.FontColor = Color.FromArgb("#263528");
        canvas.FontSize = 15;
        canvas.DrawString(
            title,
            dirtyRect.X + 14,
            dirtyRect.Y + 10,
            dirtyRect.Width - 28,
            24,
            HorizontalAlignment.Left,
            VerticalAlignment.Center);

        if (Items.Count == 0)
        {
            canvas.FontColor = Color.FromArgb("#647066");
            canvas.FontSize = 12;
            canvas.DrawString(
                "No completed orders yet",
                dirtyRect.X + 14,
                dirtyRect.Y + 72,
                dirtyRect.Width - 28,
                30,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);
            return;
        }

        var visibleItems = Items.Take(4).ToArray();
        var maxValue = visibleItems.Max(item => item.Value);
        const float rowHeight = 36;
        const float labelWidth = 92;
        const float valueWidth = 58;
        var chartLeft = dirtyRect.X + 14;
        var barLeft = chartLeft + labelWidth;
        var barWidth = Math.Max(30, dirtyRect.Width - labelWidth - valueWidth - 32);
        var top = dirtyRect.Y + 45;

        for (var index = 0; index < visibleItems.Length; index++)
        {
            var item = visibleItems[index];
            var y = top + index * rowHeight;
            var normalized = maxValue <= 0 ? 0 : (float)(item.Value / maxValue);

            canvas.FontColor = Color.FromArgb("#435047");
            canvas.FontSize = 11;
            canvas.DrawString(
                Truncate(item.Label, 14),
                chartLeft,
                y,
                labelWidth - 6,
                24,
                HorizontalAlignment.Left,
                VerticalAlignment.Center);

            canvas.FillColor = Color.FromArgb("#EDF2EB");
            canvas.FillRoundedRectangle(barLeft, y + 4, barWidth, 16, 5);

            canvas.FillColor = accentColor;
            canvas.FillRoundedRectangle(
                barLeft,
                y + 4,
                Math.Max(4, barWidth * normalized),
                16,
                5);

            canvas.FontColor = Color.FromArgb("#263528");
            canvas.FontSize = 10;
            canvas.DrawString(
                formatValue(item.Value),
                barLeft + barWidth + 5,
                y,
                valueWidth,
                24,
                HorizontalAlignment.Left,
                VerticalAlignment.Center);
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength
            ? value
            : $"{value[..(maxLength - 1)]}\u2026";
}
