namespace ControlGallery.Pages;

public partial class ShadowPage : ContentPage
{
    public ShadowPage()
    {
        InitializeComponent();
        AddPolygonsManually();
    }

    void AddPolygonsManually()
    {
        var primaryBrush = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#512BD4"));

        // Shape section polygon
        var shapePolygon = new Microsoft.Maui.Controls.Shapes.Polygon
        {
            Fill = primaryBrush, HeightRequest = 50, WidthRequest = 50,
            Points = new PointCollection
            {
                new(0, 36), new(0, 13.5), new(13.5, 0), new(36, 0),
                new(50, 13.5), new(50, 36), new(36, 50), new(13.5, 50),
            }
        };
        shapePolygonContainer.Children.Add(shapePolygon);

        // Compound shapes grid polygons
        compoundShapesGrid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
        {
            Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#663300")),
            Points = new PointCollection { new(0, 0), new(0, 50), new(25, 25) }
        });
        compoundShapesGrid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
        {
            Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#FF6600")),
            Points = new PointCollection { new(0, 0), new(50, 0), new(25, 25) }
        });
        compoundShapesGrid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
        {
            Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#FF9900")),
            Points = new PointCollection { new(50, 0), new(50, 50), new(25, 25) }
        });
        compoundShapesGrid.Children.Add(new Microsoft.Maui.Controls.Shapes.Polygon
        {
            Fill = new SolidColorBrush(Microsoft.Maui.Graphics.Color.FromArgb("#8C5D00")),
            Points = new PointCollection { new(0, 50), new(25, 25), new(50, 50) }
        });
    }
}