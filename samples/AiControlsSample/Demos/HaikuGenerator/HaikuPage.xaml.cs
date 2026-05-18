using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class HaikuPage : ContentPage
{
    public AgentContext Session { get; }

    private readonly List<HaikuData> _haikus = [];
    private int _currentIndex = -1;

    private static readonly Color[] GradientColors =
    [
        Color.FromArgb("#6366F1"),
        Color.FromArgb("#8B5CF6"),
        Color.FromArgb("#EC4899"),
        Color.FromArgb("#14B8A6"),
        Color.FromArgb("#F59E0B"),
        Color.FromArgb("#10B981"),
        Color.FromArgb("#3B82F6"),
        Color.FromArgb("#EF4444"),
    ];

    public HaikuPage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(CreateHaiku, "create_haiku",
                "Create a haiku with Japanese and English versions.")
        };

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a haiku poet. When the user asks for a haiku:
                1. Create a haiku by calling create_haiku with the Japanese and English versions.
                2. The Japanese version should be 3 lines following the 5-7-5 mora pattern.
                3. The English version should be a poetic translation (not literal).
                4. Also provide a theme color as a CSS hex color (e.g., "#6366F1").
                5. After creating the haiku, comment briefly on its meaning.

                Be creative and poetic. Each haiku should be unique and beautiful.
                """,
            Tools = [.. tools]
        };
        var agent = new UIAgent(chatClient, chatOptions);
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    [Description("Create a haiku with Japanese and English versions.")]
    private string CreateHaiku(
        [Description("JSON array of 3 Japanese lines")] string japanese_lines,
        [Description("JSON array of 3 English lines")] string english_lines,
        [Description("CSS hex color for background, e.g. '#6366F1'")] string theme_color)
    {
        var japanese = JsonSerializer.Deserialize<List<string>>(japanese_lines) ?? [];
        var english = JsonSerializer.Deserialize<List<string>>(english_lines) ?? [];

        Color bgColor;
        try
        {
            bgColor = Color.FromArgb(theme_color);
        }
        catch
        {
            bgColor = GradientColors[_haikus.Count % GradientColors.Length];
        }

        var haiku = new HaikuData
        {
            Japanese = japanese,
            English = english,
            BackgroundColor = bgColor
        };

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _haikus.Add(haiku);
            _currentIndex = _haikus.Count - 1;
            RefreshHaikuUI();
        });

        return $"Haiku #{_haikus.Count} displayed.";
    }

    private void RefreshHaikuUI()
    {
        if (_currentIndex < 0 || _currentIndex >= _haikus.Count)
        {
            EmptyState.IsVisible = true;
            HaikuContent.IsVisible = false;
            NavBar.IsVisible = false;
            return;
        }

        var haiku = _haikus[_currentIndex];
        EmptyState.IsVisible = false;
        HaikuContent.IsVisible = true;
        NavBar.IsVisible = _haikus.Count > 1;

        HaikuPanel.BackgroundColor = haiku.BackgroundColor;

        JapaneseLines.Children.Clear();
        foreach (var line in haiku.Japanese)
        {
            JapaneseLines.Children.Add(new Label
            {
                Text = line,
                FontSize = 28,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center
            });
        }

        EnglishLines.Children.Clear();
        foreach (var line in haiku.English)
        {
            EnglishLines.Children.Add(new Label
            {
                Text = line,
                FontSize = 16,
                TextColor = Colors.White,
                Opacity = 0.9,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center
            });
        }

        HaikuPosition.Text = $"{_currentIndex + 1} / {_haikus.Count}";
        PrevButton.IsEnabled = _currentIndex > 0;
        NextButton.IsEnabled = _currentIndex < _haikus.Count - 1;
    }

    private void OnPrevClicked(object? sender, EventArgs e)
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            RefreshHaikuUI();
        }
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        if (_currentIndex < _haikus.Count - 1)
        {
            _currentIndex++;
            RefreshHaikuUI();
        }
    }

    private sealed class HaikuData
    {
        public List<string> Japanese { get; init; } = [];
        public List<string> English { get; init; } = [];
        public Color BackgroundColor { get; init; } = Colors.Indigo;
    }
}
