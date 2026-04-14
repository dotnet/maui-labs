using CommunityToolkit.Maui.Markup;
using Microsoft.Maui.Controls.Shapes;
using static CommunityToolkit.Maui.Markup.GridRowsColumns;

namespace ControlGallery.Pages;

public class BorderPage : ContentPageBase
{
    protected override void Build()
    {
        this.Title = "Borders";

        this.Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children = {
                    new H1("Rectangle"),
                    new Separator(),
                    new Border{
                        StrokeShape = new Rectangle(),
                        Content = new Label().Text("Basic Rectangle").Margin(12,8)
                    },

                    new H1("Round Rectangle"),
                    new Separator(),
                    new Border{
                        StrokeShape = new RoundRectangle{ CornerRadius = 20},
                        Content = new Label().Text("Round Rectangle").Margin(12,8)
                    },

                    new H1("Border Thickness"),
                    new Separator(),
                    new Border{
                        StrokeThickness = 8,
                        StrokeShape = new Rectangle(),
                        Content = new Label().Text("StrokeThickness=8").Margin(12,8)
                    },

                    new H1("StrokeDashOffset"),
                    new Separator(),
                    new Border{
                        StrokeDashOffset = 9,
                        StrokeDashArray = new DoubleCollection{1,1},
                        StrokeShape = new Rectangle(),
                        Content = new Label().Text("StrokeDashOffset=8, StrokeDashArray=1,1").Margin(12,8)
                    },

                    new H1("StrokeLineJoin"),
                    new Separator(),
                    new Border{
                        StrokeLineJoin = PenLineJoin.Bevel,
                        StrokeDashOffset = 9,
                        StrokeDashArray = new DoubleCollection{1,1},
                        StrokeShape = new Rectangle(),
                        Content = new Label().Text("StrokeLineJoine=Bevel").Margin(12,8)
                    },

                    new H1("StrokeLineCap"),
                    new Separator(),
                    new Border{
                        StrokeLineCap = PenLineCap.Flat,
                        StrokeDashOffset = 9,
                        StrokeDashArray = new DoubleCollection{1,1},
                        StrokeShape = new Rectangle(),
                        Content = new Label().Text("StrokeLineCap=Flat").Margin(12,8)
                    },

                    new H1("Ellipse"),
                    new Separator(),
                    new Grid
                    {
                        ColumnDefinitions = Columns.Define(78, Star, Star),
                        RowDefinitions = Rows.Define(48,30),
                        ColumnSpacing = 20,
                        RowSpacing = 0,
                        Children =
                        {
                            new Border{
                                StrokeShape = new Ellipse(),
                                Stroke = Colors.Red,
                                StrokeThickness = 6,
                                Content = new Label().Text("Hey").Center().FontSize(24)
                            }
                            .Row(0).Column(0).RowSpan(2)
                            .Background(Colors.Azure)
                            .Center()
                            .Size(72)

                        }
                    },
                }
            }.DynamicResource(VisualElement.StyleProperty, "MainContainer")
        };
    }
}