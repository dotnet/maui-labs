using Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class UriHelpersTests
{
	[Theory]
	[InlineData("app://localhost/css/app.css", "app://localhost/css/app.css")]
	[InlineData("app://localhost/css/app.css?v=123", "app://localhost/css/app.css")]
	[InlineData("app://localhost/css/app.css#theme", "app://localhost/css/app.css")]
	[InlineData("app://localhost/css/app.css?v=123#theme", "app://localhost/css/app.css")]
	public void RemoveQueryAndFragment_RemovesSuffixBeforeAssetLookup(string uri, string expected)
	{
		Assert.Equal(expected, UriHelpers.RemoveQueryAndFragment(uri));
	}
}
