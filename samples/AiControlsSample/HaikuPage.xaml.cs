using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AiControlsSample;

public partial class HaikuPage : ContentPage
{
    private readonly IAgentSession _session;
    private readonly List<HaikuData> _haikus = [];
    private int _currentIndex = -1;

    // Gradient color palette for backgrounds
    private static readonly Color[] GradientColors =
    [
        Color.FromArgb("#6366F1"), // Indigo
        Color.FromArgb("#8B5CF6"), // Violet
        Color.FromArgb("#EC4899"), // Pink
        Color.FromArgb("#14B8A6"), // Teal
        Color.FromArgb("#F59E0B"), // Amber
        Color.FromArgb("#10B981"), // Emerald
        Color.FromArgb("#3B82F6"), // Blue
        Color.FromArgb("#EF4444"), // Red
    ];

    public HaikuPage(IAgentSessionFactory sessionFactory, IChatClient chatClient)
    {
        InitializeComponent();

        _session = sessionFactory.Create(chatClient);
        _session.SystemInstructions = """
            You are a haiku poet. When the user asks for a haiku:
            1. Create a haiku by calling create_haiku with the Japanese and English versions.
            2. The Japanese version should be 3 lines following the 5-7-5 mora pattern.
            3. The English version should be a poetic translation (not literal).
            4. Also provide a theme color as a CSS hex color (e.g., "#6366F1").
            5. After creating the haiku, comment briefly on its meaning.
            
            Be creative and poetic. Each haiku should be unique and beautiful.
            """;

        RegisterTools();

        ChatView.Session = _session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Nature", "Write a haiku about nature"),
            new Suggestion("Ocean", "Create a haiku about the ocean"),
            new Suggestion("Spring", "Generate a haiku about spring"),
        ];
    }

    private void RegisterTools()
    {
        [Description("Create a haiku with Japanese and English versions.")]
        string create_haiku(
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

        _session.RegisterTools(AIFunctionFactory.Create(create_haiku));
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

        // Populate Japanese lines
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

        // Populate English lines
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

        // Navigation
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
