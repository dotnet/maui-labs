using System.Text.Encodings.Web;
using System.Threading.Channels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;
using WebView = WebKit.WebView;

namespace Platform.Maui.Linux.Gtk4.BlazorWebView;

/// <summary>
/// WebViewManager implementation for Linux using WebKitGTK.
/// Based on the DevToys.Linux implementation pattern.
/// </summary>
public class GtkWebViewManager : WebViewManager
{
	private readonly GtkBlazorWebView _blazorWebView;
	private readonly WebView _webview;
	private readonly string _contentRootRelativeToAppRoot;
	private readonly Channel<string> _channel;

	internal GtkWebViewManager(
		Uri baseUri,
		GtkBlazorWebView blazorWebView,
		IServiceProvider provider,
		IFileProvider fileProvider,
		JSComponentConfigurationStore jsComponents,
		string contentRootRelativeToAppRoot,
		string hostPageRelativePath)
		: base(
			provider,
			Microsoft.AspNetCore.Components.Dispatcher.CreateDefault(),
			baseUri,
			fileProvider,
			jsComponents,
			hostPageRelativePath)
	{
		_blazorWebView = blazorWebView;
		_webview = blazorWebView.WebKitWebView;
		_contentRootRelativeToAppRoot = contentRootRelativeToAppRoot;

		// Channel-based message pump for thread-safe JS evaluation
		// Pattern from DevToys.Linux
		_channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false
		});
		Task.Run(SendMessagePump);
	}

	internal bool TryGetResponseContentInternal(
		string uri,
		bool allowFallbackOnHostPage,
		out int statusCode,
		out string statusMessage,
		out Stream content,
		out IDictionary<string, string> headers)
	{
		return TryGetResponseContent(
			uri,
			allowFallbackOnHostPage,
			out statusCode,
			out statusMessage,
			out content,
			out headers);
	}

	internal void MessageReceivedInternal(Uri uri, string message)
	{
		MessageReceived(uri, message);
	}

	protected override ValueTask DisposeAsyncCore()
	{
		try
		{
			_channel.Writer.Complete();
		}
		catch
		{
			// Ignore
		}

		return base.DisposeAsyncCore();
	}

	protected override void NavigateCore(Uri absoluteUri)
	{
		_webview.LoadUri(absoluteUri.ToString());
	}

	protected override void SendMessage(string message)
	{
		string messageJsStringLiteral = JavaScriptEncoder.Default.Encode(message);
		string script = $"__dispatchMessageCallback(\"{messageJsStringLiteral}\")";

		while (!_channel.Writer.TryWrite(script))
		{
			Thread.Sleep(200);
		}
	}

	private async Task SendMessagePump()
	{
		ChannelReader<string> reader = _channel.Reader;
		try
		{
			while (true)
			{
				string script = await reader.ReadAsync();
				// WebKit API calls must be on the GTK main thread
				GLib.Functions.IdleAdd(0, () =>
				{
					_webview.EvaluateJavascriptAsync(script);
					return false;
				});
			}
		}
		catch (ChannelClosedException) { }
	}
}
