namespace Microsoft.Maui.DevFlow.Blazor;

internal static class WebViewNavigation
{
    internal static bool IsAbsoluteUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (!uri.IsFile || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase));

    internal static string CreateRelativeNavigationScript(string url)
    {
        var escapedUrl = url.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
        return $$"""
            (function() {
                const target = '{{escapedUrl}}';
                try {
                    if (window.Blazor && typeof window.Blazor.navigateTo === 'function') {
                        window.Blazor.navigateTo(target);
                        return 'blazor';
                    }
                } catch {}

                try {
                    history.pushState({}, '', target);
                    window.dispatchEvent(new PopStateEvent('popstate'));
                    return 'history';
                } catch {}

                location.href = target;
                return 'location';
            })()
            """;
    }
}
