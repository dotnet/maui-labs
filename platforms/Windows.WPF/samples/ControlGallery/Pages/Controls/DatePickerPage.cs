namespace ControlGallery.Pages;

public class DatePickerPage : ContentPageBase
{
    protected override void Build()
    {
        this.Title = "DatePicker";

        this.Content = new ScrollView{
            Content = new VerticalStackLayout{
                Spacing = 12,
                Children = {
                    new H1("Default"),
                    new Separator(),
                    new DatePicker(),
    
                }
            }.DynamicResource(VisualElement.StyleProperty, "MainContainer")
        };
    }
}