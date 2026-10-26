namespace AIExtensions.Sample.ChatPlayground;

internal readonly record struct PlaygroundLayoutMetrics(double Width)
{
    public const double SidebarWidth = 330;
    public const double DiagnosticsWidth = 390;
    private const double OverlayDismissWidth = 44;

    public bool IsCompact => Width > 0 && Width < 760;
    public bool DiagnosticsOverlay => Width < 1200;
    public double SettingsPanelWidth => IsCompact ? Math.Max(0, Math.Min(SidebarWidth, Width)) : SidebarWidth;
    public double DiagnosticsPanelWidth => Math.Max(0, Math.Min(DiagnosticsWidth,
        Width - (DiagnosticsOverlay ? OverlayDismissWidth : 24)));

    public int MainColumnSpan(bool diagnosticsVisible) =>
        IsCompact ? 3 : diagnosticsVisible && !DiagnosticsOverlay ? 1 : 2;
}
