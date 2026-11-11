using System.ComponentModel;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// One scenario's workspace: its chat, and a panel when the scenario has state to show. On a wide screen the panel sits
/// beside the chat; on a narrow one it sits above the chat and collapses to its title.
/// </summary>
public partial class ScenarioView : ContentView
{
    private const double WideWidth = 860;
    private const double PanelWidth = 360;

    private readonly AccentColorController? _accent;
    private bool? _isWide;
    private bool _isExpanded;

    /// <summary>Initializes the workspace of one scenario.</summary>
    public ScenarioView(ScenarioSessionViewModel session, AccentColorController accent)
    {
        InitializeComponent();
        BindingContext = session;

        var (title, panel) = CreatePanel(session, accent);
        if (panel is not null)
        {
            PanelTitleLabel.Text = title;
            PanelContent.Content = panel;
            PanelCard.IsVisible = true;
        }

        if (panel?.BindingContext is DocumentPanelViewModel document)
        {
            // A proposal waits for review in the chat, and the panel shows what it changes.
            document.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DocumentPanelViewModel.IsPending) && document.IsPending)
                    ExpandPanel();
            };
        }

        if (session.Scenario is FrontendToolsScenario)
        {
            // The agent's UI actions recolor this scenario through the chat's accent color.
            _accent = accent;
            accent.PropertyChanged += AccentChanged;
            if (Application.Current is { } application)
                application.RequestedThemeChanged += (_, _) => ApplyAccent();
            ApplyAccent();
        }

        SizeChanged += (_, _) => Arrange();
    }

    private static (string Title, View? Panel) CreatePanel(ScenarioSessionViewModel session, AccentColorController accent)
    {
        switch (session.Scenario)
        {
            case FrontendToolsScenario:
                return ("Accent color", new AccentPanel { BindingContext = accent });
            case PlanProgressScenario:
                return ("Plan", new PlanPanel { BindingContext = new PlanPanelViewModel(session) });
            case SharedStateScenario:
                return ("Recipe", new RecipePanel { BindingContext = new RecipeEditorViewModel(session) });
            case PredictiveStateScenario:
                return ("Document", new DocumentPanel { BindingContext = new DocumentPanelViewModel(session) });
            case PersistenceScenario:
                return ("Saved conversation", new PersistencePanel { BindingContext = new PersistencePanelViewModel(session) });
            default:
                return (string.Empty, null);
        }
    }

    private void Arrange()
    {
        if (Width <= 0 || !PanelCard.IsVisible)
        {
            Grid.SetRow(ChatCard, 0);
            Grid.SetColumn(ChatCard, 0);
            return;
        }

        var wide = Width >= WideWidth;
        if (wide != _isWide)
        {
            _isWide = wide;
            Workspace.RowDefinitions.Clear();
            Workspace.ColumnDefinitions.Clear();
            if (wide)
            {
                Workspace.ColumnDefinitions.Add(new ColumnDefinition(PanelWidth));
                Workspace.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                Grid.SetRow(PanelCard, 0);
                Grid.SetColumn(PanelCard, 0);
                Grid.SetRow(ChatCard, 0);
                Grid.SetColumn(ChatCard, 1);
            }
            else
            {
                Workspace.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Workspace.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                Grid.SetRow(PanelCard, 0);
                Grid.SetColumn(PanelCard, 0);
                Grid.SetRow(ChatCard, 1);
                Grid.SetColumn(ChatCard, 0);
            }
        }

        // On a narrow screen the panel takes at most part of the height, so the chat stays usable.
        PanelChevron.IsVisible = !wide;
        PanelContent.IsVisible = wide || _isExpanded;
        PanelChevron.Text = _isExpanded ? "\u25B4" : "\u25BE";
        PanelCard.MaximumHeightRequest = wide ? double.PositiveInfinity : Math.Max(160, Height * 0.45);
        SemanticProperties.SetDescription(PanelHeader, wide
            ? PanelTitleLabel.Text
            : $"{PanelTitleLabel.Text}, {(_isExpanded ? "expanded" : "collapsed")}. Tap to {(_isExpanded ? "collapse" : "expand")}.");

        // The narrow panel sits in an Auto row, and showing its content does not make iOS measure that row again by
        // itself: the panel would keep the height of its title. Measure the panel and the workspace again explicitly.
        ((IView)PanelCard).InvalidateMeasure();
        ((IView)Workspace).InvalidateMeasure();
    }

    private void PanelHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (_isWide == true)
            return;
        _isExpanded = !_isExpanded;
        Arrange();
    }

    /// <summary>Opens the panel on a narrow screen, for state the user should see now.</summary>
    internal void ExpandPanel()
    {
        if (_isExpanded)
            return;
        _isExpanded = true;
        Arrange();
    }

    private void AccentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccentColorController.Color))
            ApplyAccent();
    }

    private void ApplyAccent()
    {
        if (_accent is null)
            return;

        // Always set here: a resource removed from a dictionary does not update what already uses it. Without a
        // custom color this repeats the theme's own accent, again after the theme changes.
        if (_accent.Color is { } color)
        {
            Resources[AIChatThemeKeys.AccentColor] = color;
            Resources[AIChatThemeKeys.OnAccentColor] = _accent.OnAccent;
        }
        else if (Application.Current?.Resources is { } resources &&
            resources.TryGetValue(AIChatThemeKeys.AccentColor, out var accent) &&
            resources.TryGetValue(AIChatThemeKeys.OnAccentColor, out var onAccent))
        {
            Resources[AIChatThemeKeys.AccentColor] = accent;
            Resources[AIChatThemeKeys.OnAccentColor] = onAccent;
        }
    }
}
