using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.MacOS.Controls;

/// <summary>
/// A custom BlazorWebView control for macOS (AppKit) that hosts Blazor components
/// in a WKWebView. This avoids the MAUI BlazorWebView control which internally
/// casts to its own handler type and fails on AppKit.
/// </summary>
public class MacOSBlazorWebView : View
{
    public static readonly BindableProperty HostPageProperty =
        BindableProperty.Create(nameof(HostPage), typeof(string), typeof(MacOSBlazorWebView));

    public static readonly BindableProperty StartPathProperty =
        BindableProperty.Create(nameof(StartPath), typeof(string), typeof(MacOSBlazorWebView), "/");

    /// <summary>
    /// Content insets for the WebView's scrollable content. The web content scrolls
    /// through the full WKWebView bounds, but scroll indicators and the initial
    /// content position are inset by these amounts.
    /// Uses WKWebView.ObscuredContentInsets (macOS 14+).
    /// When left at default (all zeros), the top inset is automatically calculated
    /// from the toolbar/titlebar height if the window uses FullSizeContentView.
    /// </summary>
    public static readonly BindableProperty ContentInsetsProperty =
        BindableProperty.Create(nameof(ContentInsets), typeof(Thickness), typeof(MacOSBlazorWebView), default(Thickness));

    /// <summary>
    /// When true, hides the WKWebView's internal scroll pocket overlay views
    /// (NSScrollPocket, BackdropView) that create a harsh white bar at the top
    /// of the content area when the view extends behind the toolbar.
    /// Enable this when using FullSizeContentView with a toolbar and
    /// TitlebarAppearsTransparent=false to get a clean toolbar appearance.
    /// Default is false (standard WKWebView behavior).
    /// </summary>
    public static readonly BindableProperty HideScrollPocketOverlayProperty =
        BindableProperty.Create(nameof(HideScrollPocketOverlay), typeof(bool), typeof(MacOSBlazorWebView), false);

    public string? HostPage
    {
        get => (string?)GetValue(HostPageProperty);
        set => SetValue(HostPageProperty, value);
    }

    public string StartPath
    {
        get => (string)GetValue(StartPathProperty);
        set => SetValue(StartPathProperty, value);
    }

    /// <summary>
    /// Gets or sets the content insets (top, left, bottom, right) for the WebView.
    /// Content scrolls through the full bounds but is visually inset by these amounts.
    /// When left at default (all zeros), the top inset is automatically calculated
    /// from the toolbar/titlebar height.
    /// </summary>
    public Thickness ContentInsets
    {
        get => (Thickness)GetValue(ContentInsetsProperty);
        set => SetValue(ContentInsetsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to hide the WKWebView's internal scroll pocket overlay
    /// that creates a harsh white bar when content extends behind the toolbar.
    /// </summary>
    public bool HideScrollPocketOverlay
    {
        get => (bool)GetValue(HideScrollPocketOverlayProperty);
        set => SetValue(HideScrollPocketOverlayProperty, value);
    }

    public List<BlazorRootComponent> RootComponents { get; } = new();
}

public class BlazorRootComponent
{
    public string? Selector { get; set; }
    public Type? ComponentType { get; set; }
    public IDictionary<string, object?>? Parameters { get; set; }
}
