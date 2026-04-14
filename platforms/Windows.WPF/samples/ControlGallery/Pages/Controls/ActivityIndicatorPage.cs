namespace ControlGallery.Pages;

public class ActivityIndicatorPage : ContentPageBase
{
    protected override void Build()
    {
        this.Title = "ActivityIndicator";

        this.Content = new ScrollView{
            Content = new VerticalStackLayout{
                Spacing = 12,
                Children = {
                    new H1("Default"),
                    new Separator(),
                    new ActivityIndicator
                    { 
                        IsRunning = true
                    }
                        .Start(),

                    new H1("Styled"),
                    new Separator(),
                    new ActivityIndicator{
                        IsRunning = true,
                        Color = AppColors.Cyan300Accent
                    }
                        .Start(),         
                }
            }.DynamicResource(VisualElement.StyleProperty, "MainContainer")
        };
    }
}