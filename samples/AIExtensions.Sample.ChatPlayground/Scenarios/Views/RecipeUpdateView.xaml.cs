using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows an <c>update_recipe</c> call as a short note: the state mapper already made it the recipe.</summary>
public partial class RecipeUpdateView : ScenarioCardView
{
    public RecipeUpdateView() => InitializeComponent();

    protected override void Refresh()
    {
        if (TitleLabel is null || Block is not FunctionInvocationContentBlock call)
            return;

        var recipe = call.Arguments is { } arguments && arguments.TryGetValue("recipe", out var value)
            ? ScenarioJson.Read<RecipeState>(value)
            : null;
        TitleLabel.Text = call.HasResult || !IsRunning(call)
            ? "Updated the shared recipe"
            : "Updating the shared recipe\u2026";
        DetailLabel.Text = recipe is null
            ? string.Empty
            : $"{recipe.Title} \u00B7 {recipe.Ingredients.Count} ingredients \u00B7 {recipe.CookingTime}";
        DetailLabel.IsVisible = DetailLabel.Text.Length > 0;
        SemanticProperties.SetDescription(Card, $"{TitleLabel.Text}. {DetailLabel.Text}");
    }
}
