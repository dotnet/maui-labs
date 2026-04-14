using System.Collections.Immutable;
using CommunityToolkit.Maui.Markup;

namespace ControlGallery.Pages.Controls.CarouselView;

public class BasicCarouselPage : ContentPage
{
    public BasicCarouselPage()
    {
        this.Title = "Basic CarouselView";
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        Build();
    }

    private IndicatorView indicators;
    enum Rows { Carousel, Indicators, Filler }

    private void Build()
    {
        this.Content = new Grid()
        {
            RowSpacing = 16,
            RowDefinitions = GridRowsColumns.Rows.Define(
                (Rows.Carousel    , 200 ),                
                (Rows.Indicators, 75  ),
                (Rows.Filler, GridLength.Star)
            ),
            Children =
            {
                new IndicatorView
                    {
                        IndicatorColor = Colors.DarkGrey,
                        SelectedIndicatorColor = Colors.Purple,
                        IndicatorSize = 10
                    }
                    .Row(Rows.Indicators)
                    .Assign(out indicators),
                new Border
                    {
                        Stroke = Colors.Transparent,
                        StrokeThickness = 0,
                        Padding = 16,
                        Margin = 16,
                        Content = 
                            new Microsoft.Maui.Controls.CarouselView()
                                {
                                    IndicatorView = indicators
                                }
                                .ItemsSource(new string[] { "insta_04.jpg", "insta_03.jpg", "insta_02.jpg", "insta_01.jpg"})
                                .ItemTemplate (new DataTemplate(() =>
                                        new Image()
                                            .Bind (Image.SourceProperty,".")
                                            .Size(240,240)
                                            .Aspect(Aspect.AspectFill)
                                    )
                                )
                                .Size(240)
                                .Background(Colors.Transparent)
                                
                            
                        
                    }
                    .Row(Rows.Carousel)
                
                    
            }
        };
    }
}