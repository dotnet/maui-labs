using CommunityToolkit.Maui.Markup;

namespace ControlGallery.Pages;

public class EditorPage : ContentPageBase
{
    protected override void Build()
    {
        this.Title = "Editor";

        this.Content = new ScrollView {
            Content = new VerticalStackLayout{
                Spacing = 12,
                Children = {
                    new H1("Default"),
                    new Separator(),
                    new Editor(),

                    new H1("Placeholder"),
                    new Separator(),
                    new Editor().Placeholder("Placeholder"),

                    new H1("Disabled"),
                    new Separator(),
                    new Editor().Text("I am disabled").IsEnabled(false),

                    new H1("Read-only"),
                    new Separator(),
                    new Editor{ IsReadOnly = true }.Text("I am read-only"),

                    new H1("AutoSize"),
                    new Separator(),
                    new Editor{ AutoSize = EditorAutoSizeOption.TextChanges }.Placeholder("Each new line grows the Editor"),

                    new H1("Fixed Height"),
                    new Separator(),
                    new Editor().Placeholder("This tall and nore more").Height(200)

                }
            }.DynamicResource(VisualElement.StyleProperty, "MainContainer")
        };
    }
}