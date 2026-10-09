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
    public static readonly BindableProperty DiagnosticsProperty = BindableProperty.Create(
        nameof(Diagnostics), typeof(DiagnosticsViewModel), typeof(PlaygroundPageLayout),
        propertyChanged: (bindable, _, value) => ((PlaygroundPageLayout)bindable).BindDiagnostics(value as DiagnosticsViewModel));
    public static readonly BindableProperty IsDiagnosticsOpenProperty = BindableProperty.Create(
        nameof(IsDiagnosticsOpen), typeof(bool), typeof(PlaygroundPageLayout), false,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((PlaygroundPageLayout)bindable).DiagnosticsOpenChanged());
    public static readonly BindableProperty DiagnosticsAutomationPrefixProperty = BindableProperty.Create(
        nameof(DiagnosticsAutomationPrefix), typeof(string), typeof(PlaygroundPageLayout), "Chat");
    private const double SidebarWidth = 330;
    private bool _isCompact;

    public PlaygroundPageLayout()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Resize(Width);
        HeaderPanel.SizeChanged += (_, _) => ResizeDiagnostics();
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

    public DiagnosticsViewModel? Diagnostics
    {
        get => (DiagnosticsViewModel?)GetValue(DiagnosticsProperty);
        set => SetValue(DiagnosticsProperty, value);
    }

    public bool IsDiagnosticsOpen
    {
        get => (bool)GetValue(IsDiagnosticsOpenProperty);
        set => SetValue(IsDiagnosticsOpenProperty, value);
    }

    public string DiagnosticsAutomationPrefix
    {
        get => (string)GetValue(DiagnosticsAutomationPrefixProperty);
        set => SetValue(DiagnosticsAutomationPrefixProperty, value);
    }

    private void BindDiagnostics(DiagnosticsViewModel? viewModel)
    {
        DiagnosticsToggle.IsVisible = viewModel is not null;
        if (viewModel is not null)
            SetBinding(IsDiagnosticsOpenProperty, new Binding(
                nameof(DiagnosticsViewModel.IsOpen), BindingMode.TwoWay, source: viewModel));
        else
        {
            RemoveBinding(IsDiagnosticsOpenProperty);
            IsDiagnosticsOpen = false;
        }
        ResizeDiagnostics();
    }

    private void DiagnosticsOpenChanged()
    {
        if (IsDiagnosticsOpen)
            CloseSettingsClicked(this, EventArgs.Empty);
        ResizeDiagnostics();
    }

    private void CloseDiagnosticsClicked(object? sender, EventArgs e) => IsDiagnosticsOpen = false;

    private void ResizeDiagnostics()
    {
        var visible = IsDiagnosticsOpen && Diagnostics is not null;
        var overlay = Width < 1200;
        var margin = new Thickness(0, overlay ? Math.Max(0, HeaderPanel.Height) + MainPanel.RowSpacing : 0, 0, 0);
        DiagnosticsPanel.Margin = margin;
        DiagnosticsBackdrop.Margin = margin;
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
            Grid.SetColumnSpan(MainPanel, IsDiagnosticsOpen && Diagnostics is not null && width >= 1200 ? 1 : 2);
        }
    }

    private void OpenSettingsClicked(object? sender, EventArgs e)
    {
        if (_isCompact)
        {
            IsDiagnosticsOpen = false;
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
