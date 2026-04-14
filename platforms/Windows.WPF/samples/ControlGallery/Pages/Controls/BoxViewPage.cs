namespace ControlGallery.Pages;

public class BoxViewPage : ContentPageBase
{
    protected override void Build()
    {
        this.Title = "BoxViews";

        this.Content = new ScrollView{
            Content = new VerticalStackLayout{
                Spacing = 12,
                Children = {
                    new H1("100x100"),
                    new Separator(),
                    new BoxView{ Color = AppColors.Primary }.Size(100),       
                }
            }.DynamicResource(VisualElement.StyleProperty, "MainContainer")
        };
    }
}