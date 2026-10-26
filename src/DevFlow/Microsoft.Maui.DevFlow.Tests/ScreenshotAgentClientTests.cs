using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class ScreenshotAgentClientTests
{
    // Guards the test server's accept call so a missed client connection fails fast
    // instead of hanging CI indefinitely.
    private static readonly TimeSpan AcceptTimeout = TimeSpan.FromSeconds(10);

    private static readonly byte[] SamplePng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04
    };

    [Fact]
    public async Task ScreenshotResultAsync_SuccessfulCapture_ReturnsPngBytes()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream);
            Assert.Contains(
                "GET /api/v1/ui/screenshot?captureEpoch=42&registryGeneration=7",
                request,
                StringComparison.Ordinal);
            await WriteResponseAsync(
                stream,
                200,
                "OK",
                "image/png",
                SamplePng,
                new Dictionary<string, string>
                {
                    ["X-DevFlow-Capture-Epoch"] = "42",
                    ["X-DevFlow-Registry-Generation"] = "7",
                    ["X-DevFlow-Window-Id"] = "2"
                });
        });

        using var agent = new AgentClient("localhost", port);

        var result = await agent.ScreenshotResultAsync(
            window: null,
            elementId: null,
            selector: null,
            maxWidth: null,
            scale: null,
            captureEpoch: 42,
            registryGeneration: 7);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(SamplePng, result.Data);
        Assert.Null(result.Error);
        Assert.False(result.Retryable);
        Assert.Equal(42, result.CaptureEpoch);
        Assert.Equal(7, result.RegistryGeneration);
        Assert.Equal(2, result.WindowId);

        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_WindowNotFrontmost_ReturnsRetryableFailureWithSuggestions()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        const string body = """
        {
          "success": false,
          "error": "Failed to capture screenshot because the app window is not frontmost (the app is not the active application). Bring the app to the foreground and retry.",
          "reason": "window-not-frontmost",
          "details": {
            "retryable": true,
            "suggestions": [
              "Bring the MAUI app window to the foreground (click it or use the app switcher / Cmd+Tab), then retry.",
              "Ensure the app window is visible and not minimized."
            ]
          }
        }
        """;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            await WriteResponseAsync(stream, 409, "Conflict", "application/json", Encoding.UTF8.GetBytes(body));
        });

        using var agent = new AgentClient("localhost", port);

        var result = await agent.ScreenshotResultAsync();

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal("window-not-frontmost", result.Reason);
        Assert.True(result.Retryable);
        Assert.NotNull(result.Error);
        Assert.Contains("not frontmost", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Suggestions);
        Assert.Equal(2, result.Suggestions!.Count);
        Assert.Contains(result.Suggestions, s => s.Contains("foreground", StringComparison.OrdinalIgnoreCase));

        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_CaptureChangedDuringRead_Retries()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var client = await AcceptAsync(listener);
                using var stream = client.GetStream();
                await ReadRequestAsync(stream);
                if (attempt == 0)
                {
                    const string error =
                        """{"success":false,"error":"UI changed","reason":"capture-changed-during-read"}""";
                    await WriteResponseAsync(
                        stream,
                        409,
                        "Conflict",
                        "application/json",
                        Encoding.UTF8.GetBytes(error));
                }
                else
                {
                    await WriteResponseAsync(
                        stream,
                        200,
                        "OK",
                        "image/png",
                        SamplePng);
                }
            }
        });

        using var agent = new AgentClient("localhost", port);

        var result = await agent.ScreenshotResultAsync();

        Assert.True(result.Success);
        Assert.Equal(SamplePng, result.Data);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotAsync_OnError_ReturnsNullForBackCompat()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        const string body = """
        { "success": false, "error": "Failed to capture screenshot", "reason": "window-not-frontmost", "details": { "retryable": true } }
        """;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            await WriteResponseAsync(stream, 409, "Conflict", "application/json", Encoding.UTF8.GetBytes(body));
        });

        using var agent = new AgentClient("localhost", port);

        var data = await agent.ScreenshotAsync();

        Assert.Null(data);

        await serverTask;
    }

    [Fact]
    public async Task ScreenshotAsync_OnSuccess_ReturnsBytesForBackCompat()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            await WriteResponseAsync(stream, 200, "OK", "image/png", SamplePng);
        });

        using var agent = new AgentClient("localhost", port);

        var data = await agent.ScreenshotAsync();

        Assert.NotNull(data);
        Assert.Equal(SamplePng, data);

        await serverTask;
    }

    private static async Task<TcpClient> AcceptAsync(TcpListener listener)
    {
        using var cts = new CancellationTokenSource(AcceptTimeout);
        return await listener.AcceptTcpClientAsync(cts.Token);
    }

    private static async Task<string> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        using var request = new MemoryStream();

        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
                break;

            request.Write(buffer, 0, read);
            var bytes = request.ToArray();

            // GET requests have no body; stop once headers are fully received.
            if (IndexOf(bytes, HeaderTerminator) >= 0)
                break;
        }

        return Encoding.UTF8.GetString(request.ToArray());
    }

    private static readonly byte[] HeaderTerminator = Encoding.ASCII.GetBytes("\r\n\r\n");

    private static int IndexOf(byte[] source, byte[] pattern)
    {
        for (var i = 0; i <= source.Length - pattern.Length; i++)
        {
            var found = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
                return i;
        }

        return -1;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string statusText,
        string contentType,
        byte[] body,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var builder = new StringBuilder()
            .Append($"HTTP/1.1 {statusCode} {statusText}\r\n")
            .Append($"Content-Type: {contentType}\r\n")
            .Append($"Content-Length: {body.Length}\r\n");
        if (headers is not null)
        {
            foreach (var header in headers)
                builder.Append($"{header.Key}: {header.Value}\r\n");
        }
        builder.Append("Connection: close\r\n\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(body);
    }
}
