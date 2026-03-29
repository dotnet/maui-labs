using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Authentication;

namespace Platform.Maui.Linux.Gtk4.Essentials.Authentication;

public class LinuxWebAuthenticator : IWebAuthenticator
{
	public async Task<WebAuthenticatorResult> AuthenticateAsync(
		WebAuthenticatorOptions webAuthenticatorOptions, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(webAuthenticatorOptions);
		var callbackUrl = webAuthenticatorOptions.CallbackUrl
			?? throw new ArgumentException("CallbackUrl is required.");
		var authUrl = webAuthenticatorOptions.Url
			?? throw new ArgumentException("Url is required.");

		// Start a local HTTP listener to receive the callback
		var port = GetAvailablePort();
		var redirectUri = new Uri($"http://127.0.0.1:{port}/");

		// Replace the callback in the auth URL
		var authUriBuilder = new UriBuilder(authUrl);
		var query = System.Web.HttpUtility.ParseQueryString(authUriBuilder.Query);
		query["redirect_uri"] = redirectUri.ToString();
		authUriBuilder.Query = query.ToString();

		using var listener = new HttpListener();
		listener.Prefixes.Add(redirectUri.ToString());
		listener.Start();

		// Open browser
		Process.Start(new ProcessStartInfo("xdg-open", authUriBuilder.Uri.AbsoluteUri)
			{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });

		// Wait for callback
		var contextTask = listener.GetContextAsync();
		using var reg = cancellationToken.Register(() => listener.Stop());

		try
		{
			var context = await contextTask;
			var responseUrl = context.Request.Url;

			// Send a simple response to the browser
			var responseBytes = System.Text.Encoding.UTF8.GetBytes(
				"<html><body><h1>Authentication complete</h1><p>You can close this window.</p></body></html>");
			context.Response.ContentType = "text/html";
			context.Response.ContentLength64 = responseBytes.Length;
			await context.Response.OutputStream.WriteAsync(responseBytes, cancellationToken);
			context.Response.Close();

			if (responseUrl is null)
				throw new InvalidOperationException("No response URL received.");

			// Parse the query/fragment for tokens
			var responseQuery = System.Web.HttpUtility.ParseQueryString(responseUrl.Query);
			var properties = new Dictionary<string, string>();
			foreach (string key in responseQuery.AllKeys)
			{
				if (key is not null)
					properties[key] = responseQuery[key] ?? "";
			}

			return new WebAuthenticatorResult(properties);
		}
		catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
		{
			throw new OperationCanceledException(cancellationToken);
		}
	}

	public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions)
		=> AuthenticateAsync(webAuthenticatorOptions, CancellationToken.None);

	private static int GetAvailablePort()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		return ((IPEndPoint)socket.LocalEndPoint!).Port;
	}
}
