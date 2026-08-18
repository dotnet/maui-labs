using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// Minimal HTTP/1.1 agent stand-in used to exercise <c>AgentClient</c> end to end over a real
/// socket. Written against APIs that exist on .NET Framework as well as modern .NET, because the
/// whole point of these tests is that the same source runs on both target families.
/// </summary>
internal sealed class FakeAgent : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<RecordedRequest, Response> _handler;
    private readonly List<RecordedRequest> _requests = new();
    private readonly object _sync = new();
    private volatile bool _stopped;

    private FakeAgent(TcpListener listener, Func<RecordedRequest, Response> handler)
    {
        _listener = listener;
        _handler = handler;
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Requests the agent has served, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_sync)
                return _requests.ToArray();
        }
    }

    public static FakeAgent Start(Func<RecordedRequest, Response> handler)
        => Start(IPAddress.Loopback, handler);

    public static FakeAgent Start(IPAddress address, Func<RecordedRequest, Response> handler)
    {
        var listener = new TcpListener(address, 0);
        listener.Start();
        return new FakeAgent(listener, handler);
    }

    /// <summary>Always answers with the same JSON body and a 200 status.</summary>
    public static FakeAgent StartJson(string json)
        => Start(_ => Response.Json(json));

    private async Task AcceptLoopAsync()
    {
        while (!_stopped)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { break; }
            catch (InvalidOperationException) { break; }
            catch (SocketException) { break; }

            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request is null)
                    return;

                lock (_sync)
                    _requests.Add(request);

                var response = _handler(request);
                var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
                var header = new StringBuilder()
                    .Append("HTTP/1.1 ").Append(response.StatusCode).Append(" ").Append(ReasonPhrase(response.StatusCode)).Append("\r\n")
                    .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
                    .Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n")
                    .Append("Connection: close\r\n\r\n")
                    .ToString();
                var headerBytes = Encoding.UTF8.GetBytes(header);

                await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Client went away mid-exchange; irrelevant to the assertions.
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<RecordedRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var received = new MemoryStream();
        int headerEnd;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read <= 0)
                return null;

            received.Write(buffer, 0, read);
            headerEnd = IndexOfHeaderEnd(received.ToArray());
            if (headerEnd >= 0)
                break;
        }

        var raw = received.ToArray();
        var headerText = Encoding.UTF8.GetString(raw, 0, headerEnd);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var separator = lines[i].IndexOf(':');
            if (separator > 0)
                headers[lines[i].Substring(0, separator).Trim()] = lines[i].Substring(separator + 1).Trim();
        }

        var bodyStart = headerEnd + 4;
        var contentLength = headers.TryGetValue("Content-Length", out var lengthText)
            && int.TryParse(lengthText, out var parsed) ? parsed : 0;

        var body = new MemoryStream();
        body.Write(raw, bodyStart, raw.Length - bodyStart);
        while (body.Length < contentLength)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read <= 0)
                break;
            body.Write(buffer, 0, read);
        }

        var target = requestLine[1];
        var queryStart = target.IndexOf('?');
        return new RecordedRequest(
            requestLine[0],
            queryStart >= 0 ? target.Substring(0, queryStart) : target,
            queryStart >= 0 ? target.Substring(queryStart + 1) : string.Empty,
            target,
            headers,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static int IndexOfHeaderEnd(byte[] data)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] == (byte)'\r' && data[i + 1] == (byte)'\n'
                && data[i + 2] == (byte)'\r' && data[i + 3] == (byte)'\n')
                return i;
        }

        return -1;
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        _ => "Status"
    };

    public void Dispose()
    {
        _stopped = true;
        try
        {
            _listener.Stop();
        }
        catch
        {
            // Already torn down.
        }
    }

    internal sealed class RecordedRequest
    {
        public RecordedRequest(
            string method,
            string path,
            string query,
            string target,
            Dictionary<string, string> headers,
            string body)
        {
            Method = method;
            Path = path;
            Query = query;
            Target = target;
            Headers = headers;
            Body = body;
        }

        public string Method { get; }

        /// <summary>Request path without the query string.</summary>
        public string Path { get; }

        /// <summary>Query string without the leading <c>?</c>.</summary>
        public string Query { get; }

        /// <summary>Full request target as it appeared on the request line.</summary>
        public string Target { get; }

        public Dictionary<string, string> Headers { get; }

        public string Body { get; }
    }

    internal sealed class Response
    {
        private Response(int statusCode, string body, string contentType)
        {
            StatusCode = statusCode;
            Body = body;
            ContentType = contentType;
        }

        public int StatusCode { get; }

        public string Body { get; }

        public string ContentType { get; }

        public static Response Json(string body, int statusCode = 200)
            => new Response(statusCode, body, "application/json");
    }
}
