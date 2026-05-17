using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

public partial class SharedStatePage : ContentPage
{
    public ChatSession ChatSession { get; }

    private readonly List<(string Icon, string Name, string Amount)> _ingredients = [
        ("🍝", "Pasta", "200g"),
        ("🫑", "Bell Pepper", "1"),
        ("🧅", "Onion", "1"),
        ("🧄", "Garlic", "3 cloves"),
        ("🫒", "Olive Oil", "2 tbsp"),
    ];

    public SharedStatePage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(UpdateRecipe, "update_recipe",
                "Update the recipe with improved values. Call this to modify the recipe form.")
        };

        ChatSession = new ChatSession(tools, chatClient)
        {
            SystemPrompt = """
                You are a recipe copilot. The user is editing a recipe and you help improve it.
                When you want to update the recipe, call the update_recipe tool with the improved values.
                Always explain what you changed and why.
                Keep the recipe practical and delicious.
                """
        };

        InitializeComponent();
        RefreshIngredientsUI();
    }

    [Description("Update the recipe with improved values. Call this to modify the recipe form.")]
    private string UpdateRecipe(
        [Description("Recipe title")] string title,
        [Description("Skill level: Beginner, Intermediate, or Advanced")] string skill_level,
        [Description("Cooking time, e.g. '30 min'")] string cooking_time,
        [Description("JSON array of ingredients: [{\"icon\":\"emoji\",\"name\":\"...\",\"amount\":\"...\"}]")] string ingredients_json)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecipeTitleEntry.Text = title;

            for (int i = 0; i < SkillPicker.Items.Count; i++)
            {
                if (string.Equals(SkillPicker.Items[i], skill_level, StringComparison.OrdinalIgnoreCase))
                {
                    SkillPicker.SelectedIndex = i;
                    break;
                }
            }

            for (int i = 0; i < TimePicker.Items.Count; i++)
            {
                if (string.Equals(TimePicker.Items[i], cooking_time, StringComparison.OrdinalIgnoreCase))
                {
                    TimePicker.SelectedIndex = i;
                    break;
                }
            }

            try
            {
                var ingredients = JsonSerializer.Deserialize<List<JsonElement>>(ingredients_json) ?? [];
                _ingredients.Clear();
                foreach (var ing in ingredients)
                {
                    var icon = ing.TryGetProperty("icon", out var ic) ? ic.GetString() ?? "🍽️" : "🍽️";
                    var name = ing.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var amount = ing.TryGetProperty("amount", out var a) ? a.GetString() ?? "" : "";
                    _ingredients.Add((icon, name, amount));
                }
                RefreshIngredientsUI();
            }
            catch
            {
                // Ignore JSON parse errors
            }
        });

        return "Recipe updated successfully.";
    }

    private void RefreshIngredientsUI()
    {
        IngredientsLayout.Children.Clear();
        foreach (var (icon, name, amount) in _ingredients)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Children.Add(new Label { Text = icon, FontSize = 16, VerticalOptions = LayoutOptions.Center });
            row.Children.Add(new Label { Text = name, FontSize = 13, VerticalOptions = LayoutOptions.Center });
            row.Children.Add(new Label { Text = amount, FontSize = 12, Opacity = 0.6, VerticalOptions = LayoutOptions.Center });
            IngredientsLayout.Children.Add(row);
        }
    }

    private void OnAddIngredientClicked(object? sender, EventArgs e)
    {
        _ingredients.Add(("🍽️", "New Ingredient", ""));
        RefreshIngredientsUI();
    }

    private async void OnImproveClicked(object? sender, EventArgs e)
    {
        var recipeJson = BuildRecipeJson();
        await ChatSession.SendAsync($"Here is my current recipe, please improve it:\n{recipeJson}");
    }

    private string BuildRecipeJson()
    {
        var ingredients = _ingredients.Select(i => new { icon = i.Icon, name = i.Name, amount = i.Amount });
        var recipe = new
        {
            title = RecipeTitleEntry.Text,
            skill_level = SkillPicker.SelectedItem?.ToString() ?? "Beginner",
            cooking_time = TimePicker.SelectedItem?.ToString() ?? "30 min",
            ingredients
        };
        return JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });
    }
}
