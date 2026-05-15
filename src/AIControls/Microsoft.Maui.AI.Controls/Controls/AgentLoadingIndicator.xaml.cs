namespace Microsoft.Maui.AI.Controls.Controls;

/// <summary>
/// An animated three-dot loading indicator that pulses when <see cref="IsActive"/> is <c>true</c>.
/// </summary>
public partial class AgentLoadingIndicator : ContentView
{
    private CancellationTokenSource? _animationCts;

    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(
            nameof(IsActive),
            typeof(bool),
            typeof(AgentLoadingIndicator),
            false,
            propertyChanged: OnIsActiveChanged);

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public AgentLoadingIndicator()
    {
        InitializeComponent();
    }

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AgentLoadingIndicator indicator)
        {
            if (newValue is true)
            {
                indicator.IsVisible = true;
                indicator.StartAnimation();
            }
            else
            {
                indicator.StopAnimation();
                indicator.IsVisible = false;
            }
        }
    }

    private void StartAnimation()
    {
        StopAnimation();
        _animationCts = new CancellationTokenSource();
        var token = _animationCts.Token;

        _ = AnimateDotsAsync(token);
    }

    private void StopAnimation()
    {
        _animationCts?.Cancel();
        _animationCts = null;

        Dot1.Opacity = 0.3;
        Dot2.Opacity = 0.3;
        Dot3.Opacity = 0.3;
    }

    private async Task AnimateDotsAsync(CancellationToken token)
    {
        var dots = new[] { Dot1, Dot2, Dot3 };

        while (!token.IsCancellationRequested)
        {
            for (int i = 0; i < dots.Length && !token.IsCancellationRequested; i++)
            {
                var dot = dots[i];

                await dot.FadeToAsync(1.0, 200, Easing.CubicInOut);
                if (token.IsCancellationRequested) return;

                await dot.FadeToAsync(0.3, 200, Easing.CubicInOut);
                if (token.IsCancellationRequested) return;

                await Task.Delay(80, token).ConfigureAwait(false);
            }

            if (token.IsCancellationRequested) return;
            await Task.Delay(300, token).ConfigureAwait(false);
        }
    }
}
