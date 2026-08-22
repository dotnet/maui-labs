using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace AIExtensions.Sample.Garden.Views;

public partial class AdaptiveStatusView : ContentView
{
    private AdaptiveSurfaceCoordinator? _coordinator;

    public AdaptiveStatusView()
    {
        InitializeComponent();
        HandlerChanged += OnHandlerChanged;
        HandlerChanging += OnHandlerChanging;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_coordinator is not null)
            return;

        _coordinator = Handler?.MauiContext?.Services.GetService<AdaptiveSurfaceCoordinator>();
        if (_coordinator is null)
            return;

        _coordinator.StatusChanged += OnStatusChanged;
        if (_coordinator.CurrentStatus is { } status)
            ApplyStatus(status);
    }

    private void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (_coordinator is null)
            return;

        _coordinator.StatusChanged -= OnStatusChanged;
        _coordinator = null;
    }

    private void OnStatusChanged(object? sender, AdaptiveSurfaceStatusChangedEventArgs e)
    {
        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(() => ApplyStatus(e.Status));
        else
            ApplyStatus(e.Status);
    }

    private void ApplyStatus(AdaptiveSurfaceStatus status)
    {
        Progress.IsRunning = status.IsComposing;
        Progress.IsVisible = status.IsComposing;
        IsVisible = status.IsComposing || status.IsAdapted || status.Error is not null;
        StatusLabel.Text = status.IsComposing
            ? "Adapting..."
            : $"Adapted for: {status.Explanation ?? status.Intent ?? "this page"}";
        SemanticProperties.SetDescription(
            this,
            status.Explanation is null
                ? StatusLabel.Text
                : $"{StatusLabel.Text}. {status.Explanation}");
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        if (_coordinator is not null)
            await _coordinator.ResetToStandardAsync();
    }
}
