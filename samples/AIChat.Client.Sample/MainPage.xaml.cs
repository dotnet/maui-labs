namespace AIChat.Client.Sample;

public partial class MainPage : ContentPage
{
    private bool _compact;
    private long _lastScenarioChange;
    private readonly DirectChatComposition _composition;

    public MainPage(DirectChatComposition composition)
    {
        InitializeComponent();
        _composition = composition;
        BindingContext = composition;
        SynchronizeNativeChat();
    }

    private async void OnRetry(object? sender, EventArgs e) => await _composition.RetryAsync();
    private void OnClear(object? sender, EventArgs e) => _composition.Clear();
    private async void OnAcceptDocument(object? sender, EventArgs e) => await _composition.ResolveDocumentProposalAsync(true);
    private async void OnRejectDocument(object? sender, EventArgs e) => await _composition.ResolveDocumentProposalAsync(false);
    private void OnNextScenario(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        if (now - _lastScenarioChange < 250)
            return;

        _lastScenarioChange = now;
        var currentIndex = _composition.Scenarios.IndexOf(_composition.SelectedScenario);
        _composition.SelectedScenario = _composition.Scenarios[(currentIndex + 1) % _composition.Scenarios.Count];
        SynchronizeNativeChat();
    }

    private void OnScenarioChanged(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(SynchronizeNativeChat);

    private void SynchronizeNativeChat()
    {
        NativeChat.ComposerController = _composition.ComposerController;
        NativeChat.Presentation = _composition.Presentation;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        var compact = width < 900;
        if (_compact == compact)
            return;

        _compact = compact;
        SurfacePicker.IsVisible = compact;
        if (compact)
        {
            SurfacePicker.SelectedIndex = 0;
            NativeChat.IsVisible = true;
            BlazorChat.IsVisible = false;
        }
        else
        {
            NativeChat.IsVisible = true;
            BlazorChat.IsVisible = true;
        }
    }

    private void OnSurfaceChanged(object? sender, EventArgs e)
    {
        if (!_compact)
            return;

        NativeChat.IsVisible = SurfacePicker.SelectedIndex != 1;
        BlazorChat.IsVisible = SurfacePicker.SelectedIndex == 1;
    }
}
