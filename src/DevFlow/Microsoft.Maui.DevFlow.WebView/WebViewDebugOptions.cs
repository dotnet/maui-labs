namespace Microsoft.Maui.DevFlow.WebView;

/// <summary>Options for the experimental WebView CDP bridge.</summary>
public class WebViewDebugOptions
{
    public bool Enabled { get; set; } = true;
    public bool EnableWebViewInspection { get; set; } = true;
    public bool EnableLogging { get; set; }
#if DEBUG
        = true;
#else
        = false;
#endif
}
