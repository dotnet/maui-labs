namespace Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;

internal static class UriHelpers
{
	internal static string RemoveQueryAndFragment(string uri)
	{
		int suffixIndex = uri.IndexOfAny(['?', '#']);
		return suffixIndex < 0 ? uri : uri[..suffixIndex];
	}
}
