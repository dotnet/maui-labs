using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

internal sealed class WebViewHttpFixture : IAsyncDisposable
{
    readonly HttpListener _listener = new();
    readonly Task _requests;

    public int Port { get; }

    public WebViewHttpFixture()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        Port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _requests = ServeAsync();
    }

    public string GetUrl(string platform)
        => $"http://{(platform == "android" ? "10.0.2.2" : "127.0.0.1")}:{Port}/index.html";

    async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (!_listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException) when (!_listener.IsListening)
            {
                break;
            }

            var second = context.Request.Url?.AbsolutePath == "/second.html";
            var html = second
                ? "<html><head><title>Second WebView document</title></head><body><h1 id='second-document'>Second document loaded</h1><a href='index.html'>Return</a></body></html>"
                : "<html><head><title>HTTP WebView fixture</title></head><body><h1>HTTP fixture</h1><a href='second.html'>Next document</a></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            try
            {
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Close();
        await _requests;
    }
}
