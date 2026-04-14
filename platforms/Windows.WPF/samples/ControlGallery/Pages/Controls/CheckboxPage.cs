using CommunityToolkit.Maui.Markup;

namespace ControlGallery.Pages;

public class CheckboxPage : ContentPageBase
{
    protected override void Build()
    {
        this.Title = "CheckBox";

        this.Content = new ScrollView{
            Content = new VerticalStackLayout{
                Spacing = 12,
                Children = {
                    new H1("Default"),
                    new Separator(),
                    new HorizontalStackLayout
                    {
                        Children =
                        {
                            new CheckBox(),
                            new Label().Text("Unchecked").CenterVertical()
                        }
                    },
                    new HorizontalStackLayout
                    {
                        Children =
                        {
                            new CheckBox{ IsChecked = true },
                            new Label().Text("Checked").CenterVertical()
                        }
                    },

                    new H1(text: "Disabled"),
                    new Separator(),
                    new HorizontalStackLayout
                    {
                        Children =
                        {
                            new CheckBox{IsChecked = true}.IsEnabled(false),
                            new Label().Text("Disabled").CenterVertical()
                        }
                    },
                    new HorizontalStackLayout
                    {
                        Children =
                        {
                            new CheckBox().IsEnabled(false),
                            new Label().Text("Disabled").CenterVertical()
                        }
                    },

                    new H1("Styled"),
                    new Separator(),
                    new HorizontalStackLayout
                    {
                        Children =
                        {
                            new CheckBox { Color = AppColors.Cyan300Accent, IsChecked = true },
                            new Label().Text("Color checked").CenterVertical()
                        }
                    },
                    new HorizontalStackLayout
                    {
                        Children =
                        {
                            new CheckBox { Color = AppColors.Cyan300Accent },
                            new Label().Text("Color checked").CenterVertical()
                        }
                    },
                }
            }.DynamicResource(VisualElement.StyleProperty, "MainContainer")
        };
    }
}