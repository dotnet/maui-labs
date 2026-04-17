using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.Authentication;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Authentication;

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

		ValidateCallbackUrl(callbackUrl);
		using var listener = CreateLoopbackListener(callbackUrl, out var redirectUri);

		// Honor the caller-provided callback path while normalizing the host/port
		// to the actual bound loopback endpoint so browser resolution matches the listener.
		var authUriBuilder = new UriBuilder(authUrl);
		var query = System.Web.HttpUtility.ParseQueryString(authUriBuilder.Query);
		query["redirect_uri"] = redirectUri.ToString();
		authUriBuilder.Query = query.ToString();

		cancellationToken.ThrowIfCancellationRequested();

		// Open browser
		var psi = new ProcessStartInfo("xdg-open")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		psi.ArgumentList.Add(authUriBuilder.Uri.AbsoluteUri);
		using var process = Process.Start(psi);

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

		while (true)
		{
			using var client = await listener.AcceptTcpClientAsync(timeoutCts.Token);

			var responseUrl = await TryHandleCallbackAsync(client, redirectUri, cancellationToken);
			if (responseUrl is null)
				continue;

			var responseQuery = System.Web.HttpUtility.ParseQueryString(responseUrl.Query);
			var properties = new Dictionary<string, string>();
			foreach (string? key in responseQuery.AllKeys)
			{
				if (key is not null)
					properties[key] = responseQuery[key] ?? string.Empty;
			}

			return new WebAuthenticatorResult(properties);
		}
	}

	public Task<WebAuthenticatorResult> AuthenticateAsync(WebAuthenticatorOptions webAuthenticatorOptions)
		=> AuthenticateAsync(webAuthenticatorOptions, CancellationToken.None);

	private static TcpListener CreateLoopbackListener(Uri callbackUrl, out Uri redirectUri)
	{
		var requestedPort = callbackUrl.IsDefaultPort || callbackUrl.Port == 0 ? 0 : callbackUrl.Port;
		var listener = new TcpListener(GetLoopbackAddress(callbackUrl), requestedPort);
		listener.Start();

		var endpoint = (IPEndPoint)listener.LocalEndpoint;
		var redirectUriBuilder = new UriBuilder(callbackUrl)
		{
			Scheme = Uri.UriSchemeHttp,
			Host = endpoint.Address.ToString(),
			Port = endpoint.Port,
			Path = string.IsNullOrEmpty(callbackUrl.AbsolutePath) ? "/" : callbackUrl.AbsolutePath,
			Fragment = string.Empty
		};

		redirectUri = redirectUriBuilder.Uri;
		return listener;
	}

	private static void ValidateCallbackUrl(Uri callbackUrl)
	{
		if (!string.Equals(callbackUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
			|| !callbackUrl.IsLoopback)
		{
			throw new NotSupportedException(
				"Linux WebAuthenticator requires a loopback HTTP callback URL (for example, http://127.0.0.1/callback).");
		}
	}

	private static IPAddress GetLoopbackAddress(Uri callbackUrl)
	{
		if (IPAddress.TryParse(callbackUrl.DnsSafeHost, out var address))
			return address;

		return IPAddress.Loopback;
	}

	private static async Task<Uri?> TryHandleCallbackAsync(TcpClient client, Uri redirectUri, CancellationToken cancellationToken)
	{
		using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

		var requestLine = await reader.ReadLineAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(requestLine))
		{
			await WriteResponseAsync(stream, HttpStatusCode.BadRequest, "Invalid authentication callback.", cancellationToken);
			return null;
		}

		string? headerLine;
		do
		{
			headerLine = await reader.ReadLineAsync(cancellationToken);
		}
		while (!string.IsNullOrEmpty(headerLine));

		var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
		{
			await WriteResponseAsync(stream, HttpStatusCode.MethodNotAllowed, "Only GET callbacks are supported.", cancellationToken);
			return null;
		}

		Uri responseUri;
		try
		{
			responseUri = new Uri(redirectUri, parts[1]);
		}
		catch (UriFormatException)
		{
			await WriteResponseAsync(stream, HttpStatusCode.BadRequest, "Invalid authentication callback.", cancellationToken);
			return null;
		}

		if (!string.Equals(responseUri.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal))
		{
			await WriteResponseAsync(stream, HttpStatusCode.NotFound, "Waiting for the configured authentication callback.", cancellationToken);
			return null;
		}

		await WriteResponseAsync(stream, HttpStatusCode.OK, "Authentication complete", cancellationToken);
		return responseUri;
	}

	private static async Task WriteResponseAsync(Stream stream, HttpStatusCode statusCode, string message, CancellationToken cancellationToken)
	{
		var body = statusCode == HttpStatusCode.OK
			? "<html><body><h1>Authentication complete</h1><p>You can close this window.</p></body></html>"
			: $"<html><body><h1>{(int)statusCode} {statusCode}</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>";

		var response = $"HTTP/1.1 {(int)statusCode} {statusCode}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
		var responseBytes = Encoding.UTF8.GetBytes(response);
		await stream.WriteAsync(responseBytes, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}
}
