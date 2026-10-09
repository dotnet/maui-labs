using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

internal sealed class WebViewHttpFixture : IAsyncDisposable
{
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource _shutdown = new();
    readonly Task _requests;
    readonly string _resourceDirectory;
    int _acceptedConnections;

    public int Port { get; }
    public int AcceptedConnectionCount => Volatile.Read(ref _acceptedConnections);
    public ConcurrentQueue<string> RequestPaths { get; } = new();
    public ConcurrentQueue<SocketError> ClientDisconnects { get; } = new();

    public WebViewHttpFixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MauiLabs.slnx")))
            root = root.Parent;
        _resourceDirectory = Path.Combine(
            root?.FullName ?? throw new DirectoryNotFoundException("Cannot find the sample resources."),
            "samples", "DevFlow.Sample", "Resources", "Raw", "webview-fixture");
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _requests = ServeAsync(_shutdown.Token);
    }

    public string GetUrl(string platform)
        => $"http://{(platform == "android" ? "10.0.2.2" : "127.0.0.1")}:{Port}/index.html";

    async Task ServeAsync(CancellationToken cancellationToken)
    {
        var connections = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connection = await _listener.AcceptTcpClientAsync(cancellationToken);
                Interlocked.Increment(ref _acceptedConnections);
                connections.Add(RespondAsync(connection, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await Task.WhenAll(connections);
        }
    }

    async Task RespondAsync(TcpClient connection, CancellationToken cancellationToken)
    {
        using (connection)
        {
            try
            {
                using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(cancellationToken);
                if (request is null)
                    return;
                var path = request.Split(' ').ElementAtOrDefault(1)?.Split('?')[0] ?? "";
                for (var count = 0; ; count++)
                {
                    var header = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(header))
                        break;
                    if (count >= 64)
                        throw new InvalidDataException("Too many HTTP fixture request headers.");
                }
                RequestPaths.Enqueue(path);
                var found = path is "/index.html" or "/second.html";
                var html = found
                    ? await File.ReadAllTextAsync(Path.Combine(_resourceDirectory, path[1..]), cancellationToken)
                    : "Not found";
                html = html.Replace("""<script src="_framework/hybridwebview.js"></script>""", "")
                    .Replace("<title>Hybrid WebView fixture</title>", "<title>HTTP WebView fixture</title>")
                    .Replace("data-host=\"hybrid\"", "data-host=\"standard\"")
                    .Replace("window.HybridWebView.SendRawMessage", "window.HybridWebView?.SendRawMessage");
                var bytes = Encoding.UTF8.GetBytes(html);
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, cancellationToken);
                await stream.WriteAsync(bytes, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException ex) when (ex.InnerException is SocketException
                { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
            {
                var socketError = ((SocketException)ex.InnerException).SocketErrorCode;
                ClientDisconnects.Enqueue(socketError);
                Console.WriteLine($"WebView HTTP fixture client disconnected: {socketError}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        try
        {
            await _requests;
        }
        finally
        {
            _listener.Stop();
            _shutdown.Dispose();
        }
    }
}
