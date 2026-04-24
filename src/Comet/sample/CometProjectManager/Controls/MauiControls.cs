using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Syncfusion.Maui.Toolkit;
using Syncfusion.Maui.Toolkit.Charts;
using Syncfusion.Maui.Toolkit.EffectsView;
using Syncfusion.Maui.Toolkit.Shimmer;
using Syncfusion.Maui.Toolkit.TextInputLayout;

using Grid = Microsoft.Maui.Controls.Grid;
using ColumnDefinition = Microsoft.Maui.Controls.ColumnDefinition;
using FontImageSource = Microsoft.Maui.Controls.FontImageSource;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;

namespace CometProjectManager.Controls
{
    public class ChartDataItem
    {
        public string Title { get; set; } = "";
        public int Count { get; set; }
        public Color ChartColor { get; set; } = Colors.Gray;
    }

    /// <summary>
    /// Custom legend that limits max size coefficient to 50%, matching MAUI reference LegendExt.
    /// </summary>
    public class LegendExt : ChartLegend
    {
        protected override double GetMaximumSizeCoefficient() => 0.5;
    }

    /// <summary>
    /// Syncfusion RadialBarSeries chart in a CardStyle border.
    /// </summary>
    public class CategoryChartControl : Microsoft.Maui.Controls.Border
    {
        public CategoryChartControl(List<ChartDataItem> data)
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) };
            Background = new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            StrokeThickness = 0;
            Padding = new Thickness(15);
            HeightRequest = 200;
            Margin = new Thickness(0, 12);

            var chart = new SfCircularChart();
            var legend = new LegendExt { Placement = LegendPlacement.Right };
            legend.LabelStyle = new ChartLegendLabelStyle
            {
                TextColor = Color.FromArgb("#0D0D0D"),
                Margin = new Thickness(5),
                FontSize = 18
            };
            chart.Legend = legend;

            var series = new RadialBarSeries
            {
                ItemsSource = data,
                XBindingPath = nameof(ChartDataItem.Title),
                YBindingPath = nameof(ChartDataItem.Count),
                ShowDataLabels = true,
                EnableTooltip = true,
                TrackFill = new SolidColorBrush(Color.FromArgb("#F2F2F2")),
                CapStyle = CapStyle.BothCurve,
            };

            var brushes = new List<Brush>();
            foreach (var item in data)
                brushes.Add(new SolidColorBrush(item.ChartColor));
            series.PaletteBrushes = brushes;

            chart.Series.Add(series);

            // Wrap in SfShimmer to match MAUI reference layout (shimmer inactive = transparent pass-through)
            var shimmer = new SfShimmer
            {
                BackgroundColor = Colors.Transparent,
                VerticalOptions = LayoutOptions.FillAndExpand,
                IsActive = false,
                Content = chart
            };
            Content = shimmer;
        }
    }

    /// <summary>
    /// Task row with SfEffectsView + CheckBox + Label.
    /// </summary>
    public class TaskViewControl : Microsoft.Maui.Controls.Border
    {
        public TaskViewControl(string title, bool isCompleted, Action<bool> onToggle, Action onTap)
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) };
            Background = new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            StrokeThickness = 0;
            // Match MAUI's SfShimmer CustomView min height for task rows
            MinimumHeightRequest = 75;

            var effectsView = new SfEffectsView
            {
                TouchDownEffects = SfEffects.Highlight,
                HighlightBackground = new SolidColorBrush(Color.FromArgb("#E0E0E0")),
            };

            var grid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 15,
                Padding = new Thickness(15),
            };

            var checkBox = new Microsoft.Maui.Controls.CheckBox
            {
                IsChecked = isCompleted,
                VerticalOptions = LayoutOptions.Center,
                Color = Color.FromArgb("#512BD4"),
                MinimumHeightRequest = 44,
                MinimumWidthRequest = 44,
            };
            checkBox.CheckedChanged += (s, e) => onToggle?.Invoke(e.Value);

            var label = new Microsoft.Maui.Controls.Label
            {
                Text = title,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            };

            grid.Add(checkBox, 0, 0);
            grid.Add(label, 1, 0);
            effectsView.Content = grid;

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => onTap?.Invoke();
            effectsView.GestureRecognizers.Add(tapGesture);

            Content = effectsView;
        }
    }

    /// <summary>
    /// Project card with FluentUI icon.
    /// </summary>
    public class ProjectCardControl : Microsoft.Maui.Controls.Border
    {
        public ProjectCardControl(string icon, string name, string description,
            List<(string title, Color color)> tags, Action onTap)
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) };
            Background = new SolidColorBrush(Color.FromArgb("#E0E0E0"));
            StrokeThickness = 0;
            Padding = new Thickness(15);
            WidthRequest = 200;

            var stack = new VerticalStackLayout { Spacing = 15 };

            var iconImage = new Microsoft.Maui.Controls.Image
            {
                Source = new FontImageSource
                {
                    Glyph = icon,
                    FontFamily = Fonts.FluentUI.FontFamily,
                    Color = Color.FromArgb("#0D0D0D"),
                    Size = 20,
                },
                HeightRequest = 20,
                WidthRequest = 20,
                HorizontalOptions = LayoutOptions.Start,
            };
            stack.Add(iconImage);

            stack.Add(new Microsoft.Maui.Controls.Label
            {
                Text = name.ToUpperInvariant(),
                TextColor = Color.FromArgb("#919191"),
                FontSize = 14,
            });
            stack.Add(new Microsoft.Maui.Controls.Label
            {
                Text = description,
                LineBreakMode = LineBreakMode.WordWrap,
                TextColor = Color.FromArgb("#0D0D0D"),
                FontSize = 17,
            });

            // Tags (HorizontalStackLayout matching MAUI reference)
            var tagLayout = new Microsoft.Maui.Controls.HorizontalStackLayout { Spacing = 15 };
            foreach (var (title, color) in tags)
            {
                tagLayout.Add(new Microsoft.Maui.Controls.Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
                    HeightRequest = 32,
                    StrokeThickness = 0,
                    Background = new SolidColorBrush(color),
                    Padding = Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.Android
                        ? new Thickness(12, 0)
                        : new Thickness(12, 0, 12, 8),
                    Content = new Microsoft.Maui.Controls.Label
                    {
                        Text = title,
                        TextColor = Color.FromArgb("#F2F2F2"),
                        FontSize = 14,
                        VerticalOptions = LayoutOptions.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                    }
                });
            }
            stack.Add(tagLayout);
            Content = stack;

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => onTap?.Invoke();
            GestureRecognizers.Add(tapGesture);
        }
    }

    /// <summary>
    /// FAB with FluentUI add icon.
    /// </summary>
    public class AddButtonControl : Microsoft.Maui.Controls.Button
    {
        public AddButtonControl(Action onTap)
        {
            ImageSource = new FontImageSource
            {
                Glyph = Fonts.FluentUI.add_32_regular,
                FontFamily = Fonts.FluentUI.FontFamily,
                Color = Colors.White,
                Size = 20,
            };
            BackgroundColor = Color.FromArgb("#512BD4");
            CornerRadius = 30;
            HeightRequest = 60;
            WidthRequest = 60;
            VerticalOptions = LayoutOptions.End;
            HorizontalOptions = LayoutOptions.End;
            Margin = new Thickness(30);
            Clicked += (s, e) => onTap?.Invoke();
        }
    }

    /// <summary>
    /// SfTextInputLayout wrapper.
    /// </summary>
    public class TextInputControl : SfTextInputLayout
    {
        public TextInputControl(string hint, Microsoft.Maui.Controls.View content)
        {
            Hint = hint;
            ContainerType = ContainerType.Outlined;
            ContainerBackground = new SolidColorBrush(Colors.Transparent);
            Content = content;
        }
    }
}
