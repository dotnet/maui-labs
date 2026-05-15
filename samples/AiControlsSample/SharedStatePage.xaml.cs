using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AiControlsSample;

public partial class SharedStatePage : ContentPage
{
    private readonly IAgentSession _session;
    private readonly List<(string Icon, string Name, string Amount)> _ingredients = [
        ("🍝", "Pasta", "200g"),
        ("🫑", "Bell Pepper", "1"),
        ("🧅", "Onion", "1"),
        ("🧄", "Garlic", "3 cloves"),
        ("🫒", "Olive Oil", "2 tbsp"),
    ];

    public SharedStatePage(IAgentSessionFactory sessionFactory, IChatClient chatClient)
    {
        InitializeComponent();

        _session = sessionFactory.Create(chatClient);
        _session.SystemInstructions = """
            You are a recipe copilot. The user is editing a recipe and you help improve it.
            When responding, provide a JSON state snapshot of the improved recipe
            wrapped in a DataContent block with media type "application/json".
            
            The recipe JSON format is:
            {
              "recipe": {
                "title": "...",
                "skill_level": "Beginner|Intermediate|Advanced",
                "cooking_time": "X min",
                "ingredients": [{"icon": "emoji", "name": "...", "amount": "..."}],
                "instructions": ["step 1", "step 2"]
              }
            }
            
            Always respond with both text explanation AND the recipe JSON.
            """;

        // Listen for state snapshots from the agent
        _session.StateSnapshotReceived += OnStateSnapshotReceived;

        ChatView.Session = _session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Make it healthier", "Make this recipe healthier with more vegetables"),
            new Suggestion("Add protein", "Add more protein to this recipe"),
            new Suggestion("Make it spicier", "Make this recipe spicier with chili and paprika"),
        ];

        RefreshIngredientsUI();
    }

    private void OnStateSnapshotReceived(ReadOnlyMemory<byte> data)
    {
        // Try to deserialize the recipe from the state snapshot
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("recipe", out var recipe))
            {
                MainThread.BeginInvokeOnMainThread(() => ApplyRecipe(recipe));
            }
        }
        catch
        {
            // Not a valid recipe snapshot
        }
    }

    private void ApplyRecipe(JsonElement recipe)
    {
        if (recipe.TryGetProperty("title", out var title))
            RecipeTitleEntry.Text = title.GetString();

        if (recipe.TryGetProperty("skill_level", out var skill))
        {
            var skillText = skill.GetString();
            for (int i = 0; i < SkillPicker.Items.Count; i++)
            {
                if (string.Equals(SkillPicker.Items[i], skillText, StringComparison.OrdinalIgnoreCase))
                {
                    SkillPicker.SelectedIndex = i;
                    break;
                }
            }
        }

        if (recipe.TryGetProperty("cooking_time", out var time))
        {
            var timeText = time.GetString();
            for (int i = 0; i < TimePicker.Items.Count; i++)
            {
                if (string.Equals(TimePicker.Items[i], timeText, StringComparison.OrdinalIgnoreCase))
                {
                    TimePicker.SelectedIndex = i;
                    break;
                }
            }
        }

        if (recipe.TryGetProperty("ingredients", out var ingredients) && ingredients.ValueKind == JsonValueKind.Array)
        {
            _ingredients.Clear();
            foreach (var ing in ingredients.EnumerateArray())
            {
                var icon = ing.TryGetProperty("icon", out var i) ? i.GetString() ?? "🍽️" : "🍽️";
                var name = ing.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var amount = ing.TryGetProperty("amount", out var a) ? a.GetString() ?? "" : "";
                _ingredients.Add((icon, name, amount));
            }
            RefreshIngredientsUI();
        }
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
        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent("Improve this recipe"),
            new DataContent(System.Text.Encoding.UTF8.GetBytes(recipeJson), "application/json"),
        ]);
        await _session.SendAsync(message);
    }

    private string BuildRecipeJson()
    {
        var ingredients = _ingredients.Select(i => new { icon = i.Icon, name = i.Name, amount = i.Amount });
        var recipe = new
        {
            recipe = new
            {
                title = RecipeTitleEntry.Text,
                skill_level = SkillPicker.SelectedItem?.ToString() ?? "Beginner",
                cooking_time = TimePicker.SelectedItem?.ToString() ?? "30 min",
                ingredients,
                instructions = new[] { "Cook according to instructions" }
            }
        };
        return JsonSerializer.Serialize(recipe);
    }
}
