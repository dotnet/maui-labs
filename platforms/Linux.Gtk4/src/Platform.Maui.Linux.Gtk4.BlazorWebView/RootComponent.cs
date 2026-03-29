using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView;

namespace Platform.Maui.Linux.Gtk4.BlazorWebView;

public class RootComponent
{
	public string? Selector { get; set; }
	public Type? ComponentType { get; set; }
	public IDictionary<string, object?>? Parameters { get; set; }

	internal Task AddToWebViewManagerAsync(WebViewManager webViewManager)
	{
		if (ComponentType != null && Selector != null)
		{
			return webViewManager.AddRootComponentAsync(
				ComponentType,
				Selector,
				ParameterView.FromDictionary(Parameters ?? new Dictionary<string, object?>()));
		}
		return Task.CompletedTask;
	}

	internal Task RemoveFromWebViewManagerAsync(WebViewManager webViewManager)
	{
		if (Selector != null)
		{
			return webViewManager.RemoveRootComponentAsync(Selector);
		}
		return Task.CompletedTask;
	}
}
