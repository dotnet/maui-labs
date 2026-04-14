namespace ControlGallery.SemanticControls;

public class H1 : Label
{
    public H1(string text)
    {
        this.Text(text).DynamicResource(Label.StyleProperty, "Headline");
    }

}

public class Separator : BoxView
{
    public Separator()
    {
        this.Color = AppColors.Gray700;
        this.Height(1)
            .FillHorizontal();
    }
}
