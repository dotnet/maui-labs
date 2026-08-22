using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.AI.GenerativeUI.Canvas;

/// <summary>
/// The canvas host: renders <see cref="CanvasState.CurrentView"/> (or a welcome placeholder), a busy
/// indicator, and the confirm overlay. Drop it into a page; it resolves <see cref="CanvasState"/> and
/// <see cref="IChatBridge"/> from the app service provider.
/// </summary>
public sealed class GenerativeCanvasView : ContentView
{
    private readonly ContentView _host = new();
    private readonly ActivityIndicator _busy = new()
    {
        IsVisible = false,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
    };
    private readonly Grid _confirmOverlay;
    private readonly Label _confirmTitle = new() { FontSize = 18, FontAttributes = FontAttributes.Bold };
    private readonly Label _confirmMessage = new() { FontSize = 14 };
    private readonly Button _confirmButton = new() { BackgroundColor = Color.FromArgb("#512BD4"), TextColor = Colors.White };
    private readonly Button _cancelButton = new();

    private CanvasState? _state;

    public GenerativeCanvasView()
    {
        _host.Content = BuildEmptyState();
        _confirmOverlay = BuildConfirmOverlay();

        Content = new Grid
        {
            Children = { _host, _busy, _confirmOverlay },
        };

        _confirmButton.Clicked += (_, _) => ResolveConfirm("confirm");
        _cancelButton.Clicked += (_, _) => ResolveConfirm("cancel");

        Loaded += (_, _) => Attach();
        HandlerChanged += (_, _) => Attach();
    }

    private static IServiceProvider? Services =>
        IPlatformApplication.Current?.Services
        ?? Application.Current?.Handler?.MauiContext?.Services;

    private void Attach()
    {
        if (_state is not null)
            return;
        _state = Services?.GetService<CanvasState>();
        if (_state is null)
            return;
        _state.PropertyChanged += OnStateChanged;
        Render();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        if (_state is null)
            return;

        _host.Content = _state.CurrentView ?? BuildEmptyState();
        _busy.IsVisible = _state.IsBusy;
        _busy.IsRunning = _state.IsBusy;

        _confirmOverlay.IsVisible = _state.IsConfirmVisible;
        if (_state.IsConfirmVisible)
        {
            _confirmTitle.Text = _state.ConfirmTitle;
            _confirmMessage.Text = _state.ConfirmMessage;
            _confirmButton.Text = _state.ConfirmLabel;
            _cancelButton.Text = _state.CancelLabel;
        }
    }

    private void ResolveConfirm(string intent)
    {
        _state?.HideConfirm();
        var bridge = Services?.GetService<IChatBridge>();
        _ = bridge?.RaiseIntentAsync(new UiIntent(intent));
    }

    private static View BuildEmptyState() => new VerticalStackLayout
    {
        Spacing = 8,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new Label { Text = "🌱", FontSize = 48, HorizontalOptions = LayoutOptions.Center },
            new Label
            {
                Text = "Ask about products, your cart, or orders — the view builds itself.",
                FontSize = 14,
                TextColor = Colors.Gray,
                HorizontalTextAlignment = TextAlignment.Center,
                MaximumWidthRequest = 360,
            },
        },
    };

    private Grid BuildConfirmOverlay()
    {
        var card = new Border
        {
            Padding = 20,
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 420,
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    _confirmTitle,
                    _confirmMessage,
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        HorizontalOptions = LayoutOptions.End,
                        Children = { _cancelButton, _confirmButton },
                    },
                },
            },
        };

        return new Grid
        {
            IsVisible = false,
            BackgroundColor = Color.FromArgb("#80000000"),
            Children = { card },
        };
    }
}
