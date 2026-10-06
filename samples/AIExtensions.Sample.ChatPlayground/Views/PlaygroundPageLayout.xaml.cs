namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shares the title, responsive settings, content, and input slots across playground pages.</summary>
public partial class PlaygroundPageLayout : ContentView
{
    public static readonly BindableProperty HeaderTitleProperty = BindableProperty.Create(
        nameof(HeaderTitle), typeof(string), typeof(PlaygroundPageLayout), string.Empty);
    public static readonly BindableProperty HeaderInfoProperty = BindableProperty.Create(
        nameof(HeaderInfo), typeof(string), typeof(PlaygroundPageLayout), string.Empty);
    public static readonly BindableProperty HeaderStatusProperty = BindableProperty.Create(
        nameof(HeaderStatus), typeof(string), typeof(PlaygroundPageLayout), string.Empty);
    public static readonly BindableProperty SidebarTitleProperty = BindableProperty.Create(
        nameof(SidebarTitle), typeof(string), typeof(PlaygroundPageLayout), string.Empty);
    public static readonly BindableProperty StatusAutomationIdProperty = BindableProperty.Create(
        nameof(StatusAutomationId), typeof(string), typeof(PlaygroundPageLayout), "PageStatusLabel");
    public static readonly BindableProperty SettingsContentProperty = BindableProperty.Create(
        nameof(SettingsContent), typeof(View), typeof(PlaygroundPageLayout));
    public static readonly BindableProperty MainContentProperty = BindableProperty.Create(
        nameof(MainContent), typeof(View), typeof(PlaygroundPageLayout));
    public static readonly BindableProperty InputContentProperty = BindableProperty.Create(
        nameof(InputContent), typeof(View), typeof(PlaygroundPageLayout));
    public static readonly BindableProperty HeaderActionsProperty = BindableProperty.Create(
        nameof(HeaderActions), typeof(View), typeof(PlaygroundPageLayout));
    public static readonly BindableProperty DiagnosticsContentProperty = BindableProperty.Create(
        nameof(DiagnosticsContent), typeof(View), typeof(PlaygroundPageLayout),
        propertyChanged: (bindable, _, _) => ((PlaygroundPageLayout)bindable).ResizeDiagnostics());
    private const double SidebarWidth = 330;
    private bool _isCompact;
    private bool _diagnosticsOpen;

    public PlaygroundPageLayout()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Resize(Width);
    }

    public string HeaderTitle
    {
        get => (string)GetValue(HeaderTitleProperty);
        set => SetValue(HeaderTitleProperty, value);
    }

    public string HeaderInfo
    {
        get => (string)GetValue(HeaderInfoProperty);
        set => SetValue(HeaderInfoProperty, value);
    }

    public string HeaderStatus
    {
        get => (string)GetValue(HeaderStatusProperty);
        set => SetValue(HeaderStatusProperty, value);
    }

    public string SidebarTitle
    {
        get => (string)GetValue(SidebarTitleProperty);
        set => SetValue(SidebarTitleProperty, value);
    }

    public string StatusAutomationId
    {
        get => (string)GetValue(StatusAutomationIdProperty);
        set => SetValue(StatusAutomationIdProperty, value);
    }

    public View? SettingsContent
    {
        get => (View?)GetValue(SettingsContentProperty);
        set => SetValue(SettingsContentProperty, value);
    }

    public View? MainContent
    {
        get => (View?)GetValue(MainContentProperty);
        set => SetValue(MainContentProperty, value);
    }

    public View? InputContent
    {
        get => (View?)GetValue(InputContentProperty);
        set => SetValue(InputContentProperty, value);
    }

    public View? HeaderActions
    {
        get => (View?)GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    public View? DiagnosticsContent
    {
        get => (View?)GetValue(DiagnosticsContentProperty);
        set => SetValue(DiagnosticsContentProperty, value);
    }

    public void ToggleDiagnostics()
    {
        _diagnosticsOpen = !_diagnosticsOpen;
        CloseSettingsClicked(this, EventArgs.Empty);
        ResizeDiagnostics();
    }

    public void CloseDiagnostics()
    {
        _diagnosticsOpen = false;
        ResizeDiagnostics();
    }

    private void CloseDiagnosticsClicked(object? sender, EventArgs e) => CloseDiagnostics();

    private void ResizeDiagnostics()
    {
        var visible = _diagnosticsOpen && DiagnosticsContent is not null;
        var overlay = Width < 1200;
        RootGrid.ColumnDefinitions[2].Width = visible && !overlay ? 390 : 0;
        Grid.SetColumnSpan(MainPanel, _isCompact ? 3 : visible && !overlay ? 1 : 2);
        DiagnosticsPanel.IsVisible = visible;
        DiagnosticsBackdrop.IsVisible = visible && overlay;
        Grid.SetColumn(DiagnosticsPanel, overlay ? 0 : 2);
        Grid.SetColumnSpan(DiagnosticsPanel, overlay ? 3 : 1);
        DiagnosticsPanel.HorizontalOptions = overlay ? LayoutOptions.End : LayoutOptions.Fill;
        if (overlay)
            DiagnosticsPanel.WidthRequest = Math.Max(0, Math.Min(390, Width - 24));
        else
            DiagnosticsPanel.ClearValue(VisualElement.WidthRequestProperty);
    }

    private void Resize(double width)
    {
        ResizeDiagnostics();
        var compact = width > 0 && width < 760;
        if (compact)
            SettingsPanel.WidthRequest = width < 480 ? Math.Max(0, width - 24) : SidebarWidth;
        if (compact == _isCompact)
            return;

        _isCompact = compact;
        Grid.SetRow(HeaderActionsPresenter, compact ? 1 : 0);
        Grid.SetColumn(HeaderActionsPresenter, compact ? 1 : 2);
        Grid.SetColumnSpan(HeaderActionsPresenter, compact ? 2 : 1);
        HeaderActionsPresenter.HorizontalOptions = compact ? LayoutOptions.End : LayoutOptions.Fill;
        OpenSettingsButton.IsVisible = compact;
        CloseSettingsButton.IsVisible = compact;
        SettingsBackdrop.IsVisible = false;
        if (compact)
        {
            RootGrid.ColumnDefinitions[0].Width = 0;
            SettingsPanel.IsVisible = false;
            SettingsPanel.HorizontalOptions = LayoutOptions.Start;
            Grid.SetColumnSpan(SettingsPanel, 2);
            Grid.SetColumn(MainPanel, 0);
            Grid.SetColumnSpan(MainPanel, 3);
        }
        else
        {
            RootGrid.ColumnDefinitions[0].Width = SidebarWidth;
            SettingsPanel.IsVisible = true;
            SettingsPanel.ClearValue(VisualElement.WidthRequestProperty);
            SettingsPanel.HorizontalOptions = LayoutOptions.Fill;
            Grid.SetColumnSpan(SettingsPanel, 1);
            Grid.SetColumn(MainPanel, 1);
            Grid.SetColumnSpan(MainPanel, _diagnosticsOpen && DiagnosticsContent is not null && width >= 1200 ? 1 : 2);
        }
    }

    private void OpenSettingsClicked(object? sender, EventArgs e)
    {
        if (_isCompact)
        {
            CloseDiagnostics();
            SettingsBackdrop.IsVisible = true;
            SettingsPanel.IsVisible = true;
        }
    }

    private void CloseSettingsClicked(object? sender, EventArgs e)
    {
        if (_isCompact)
        {
            SettingsBackdrop.IsVisible = false;
            SettingsPanel.IsVisible = false;
        }
    }
}
