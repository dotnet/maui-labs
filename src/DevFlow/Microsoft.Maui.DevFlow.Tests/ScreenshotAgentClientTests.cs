using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;
using SkiaSharp;

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

    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAZAAAAEsCAIAAABi1XKVAAABdElEQVR42u3BMQEAAADCoPVPbQwfoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD+Bn+3AAHo8fTrAAAAAElFTkSuQmCC");

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
                    ["X-DevFlow-Window-Id"] = "2",
                    ["X-DevFlow-Screenshot-Pixel-Width"] = "400",
                    ["X-DevFlow-Screenshot-Pixel-Height"] = "300",
                    ["X-DevFlow-Screenshot-Logical-Width"] = "200",
                    ["X-DevFlow-Screenshot-Logical-Height"] = "150",
                    ["X-DevFlow-Screenshot-Capture-Scale"] = "2",
                    ["X-DevFlow-Screenshot-Native-Scale"] = "3",
                    ["X-DevFlow-Screenshot-Format"] = "png",
                    ["X-DevFlow-Screenshot-Clamped"] = "true"
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
        Assert.Equal(400, result.PixelWidth);
        Assert.Equal(300, result.PixelHeight);
        Assert.Equal(200, result.LogicalWidth);
        Assert.Equal(150, result.LogicalHeight);
        Assert.Equal(2, result.CaptureScale);
        Assert.Equal(3, result.NativeScale);
        Assert.Equal("png", result.Format);
        Assert.True(result.ResolutionClamped == true);

        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_ResolutionOptions_UsesAdditiveHintsAndLegacyWidthFallback()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream);
            Assert.Contains("maxWidth=800", request, StringComparison.Ordinal);
            Assert.Contains("targetScale=2", request, StringComparison.Ordinal);
            Assert.Contains("maxPixelWidth=800", request, StringComparison.Ordinal);
            Assert.Contains("maxPixelHeight=600", request, StringComparison.Ordinal);
            Assert.Contains("maxPixels=480000", request, StringComparison.Ordinal);
            Assert.Contains("scale=auto", request, StringComparison.Ordinal);
            await WriteResponseAsync(stream, 200, "OK", "image/png", SamplePng);
        });

        using var agent = new AgentClient("localhost", port);
        var result = await agent.ScreenshotResultAsync(
            window: null,
            elementId: null,
            selector: null,
            maxWidth: null,
            scale: null,
            captureEpoch: null,
            registryGeneration: null,
            resolution: new ScreenshotResolutionOptions
            {
                TargetScale = 2,
                MaxPixelWidth = 800,
                MaxPixelHeight = 600,
                MaxPixels = 480_000
            });

        Assert.True(result.Success);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_ExplicitLegacyWidthWithResolutionCaps_DoesNotForceAutoScale()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream);
            Assert.Contains("maxWidth=800", request, StringComparison.Ordinal);
            Assert.Contains("maxPixels=1000000", request, StringComparison.Ordinal);
            Assert.DoesNotContain("scale=auto", request, StringComparison.Ordinal);
            await WriteResponseAsync(stream, 200, "OK", "image/png", SamplePng);
        });

        using var agent = new AgentClient("localhost", port);
        var result = await agent.ScreenshotResultAsync(
            window: null,
            elementId: null,
            selector: null,
            maxWidth: 800,
            scale: null,
            captureEpoch: null,
            registryGeneration: null,
            resolution: new ScreenshotResolutionOptions
            {
                MaxPixels = 1_000_000
            });

        Assert.True(result.Success);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_MaxEncodedBytes_RejectsOversizedChunkedResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream);
            Assert.DoesNotContain("scale=auto", request, StringComparison.Ordinal);
            Assert.DoesNotContain("maxPixels=", request, StringComparison.Ordinal);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: image/png\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "Connection: close\r\n\r\n" +
                "3\r\nabc\r\n" +
                "3\r\ndef\r\n" +
                "0\r\n\r\n"));
        });

        using var agent = new AgentClient("localhost", port);
        var result = await agent.ScreenshotResultAsync(
            window: null,
            elementId: null,
            selector: null,
            maxWidth: null,
            scale: null,
            captureEpoch: null,
            registryGeneration: null,
            resolution: new ScreenshotResolutionOptions
            {
                MaxEncodedBytes = 5
            });

        Assert.False(result.Success);
        Assert.Equal("screenshot-too-large", result.Reason);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_MaxEncodedBytes_RejectsOversizedChunkedError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 500 Internal Server Error\r\n" +
                "Content-Type: application/json\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "Connection: close\r\n\r\n" +
                "3\r\nabc\r\n" +
                "3\r\ndef\r\n" +
                "0\r\n\r\n"));
        });

        using var agent = new AgentClient("localhost", port);
        var result = await agent.ScreenshotResultAsync(
            window: null,
            elementId: null,
            selector: null,
            maxWidth: null,
            scale: null,
            captureEpoch: null,
            registryGeneration: null,
            resolution: new ScreenshotResolutionOptions
            {
                MaxEncodedBytes = 5
            });

        Assert.False(result.Success);
            Assert.Equal("screenshot-too-large", result.Reason);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_DefaultLimit_RejectsOversizedNotSupportedError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 501 Not Implemented\r\n" +
                "Content-Type: application/json\r\n" +
                "Content-Length: 70000\r\n" +
                "Connection: close\r\n\r\n"));
        });

        using var agent = new AgentClient("localhost", port);
        var result = await agent.ScreenshotResultAsync();

        Assert.False(result.Success);
        Assert.Equal("screenshot-too-large", result.Reason);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_MetadataDisagreesWithPng_IgnoresLogicalMetadata()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            await WriteResponseAsync(
                stream,
                200,
                "OK",
                "image/png",
                ValidPng,
                new Dictionary<string, string>
                {
                    ["X-DevFlow-Screenshot-Pixel-Width"] = "999",
                    ["X-DevFlow-Screenshot-Pixel-Height"] = "300",
                    ["X-DevFlow-Screenshot-Logical-Width"] = "200",
                    ["X-DevFlow-Screenshot-Logical-Height"] = "150",
                    ["X-DevFlow-Screenshot-Capture-Scale"] = "2",
                    ["X-DevFlow-Screenshot-Native-Scale"] = "2"
                });
        });

        using var agent = new AgentClient("localhost", port);
        var result = await agent.ScreenshotResultAsync();

        Assert.True(result.Success);
        Assert.Equal(400, result.PixelWidth);
        Assert.Equal(300, result.PixelHeight);
        Assert.Null(result.LogicalWidth);
        Assert.Null(result.LogicalHeight);
        Assert.Null(result.CaptureScale);
        Assert.Null(result.NativeScale);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_FlooredTallImage_PreservesValidScaleMetadata()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var png = CreatePng(294, 639);

        var serverTask = Task.Run(async () =>
        {
            using var client = await AcceptAsync(listener);
            using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            await WriteResponseAsync(
                stream,
                200,
                "OK",
                "image/png",
                png,
                new Dictionary<string, string>
                {
                    ["X-DevFlow-Screenshot-Pixel-Width"] = "294",
                    ["X-DevFlow-Screenshot-Pixel-Height"] = "639",
                    ["X-DevFlow-Screenshot-Logical-Width"] = "393",
                    ["X-DevFlow-Screenshot-Logical-Height"] = "852",
                    ["X-DevFlow-Screenshot-Capture-Scale"] = "0.7480916030534351",
                    ["X-DevFlow-Screenshot-Native-Scale"] = "3",
                    ["X-DevFlow-Screenshot-Clamped"] = "true"
                });
        });

        using var agent = new AgentClient("localhost", port);
        var result = await agent.ScreenshotResultAsync();

        Assert.True(result.Success);
        Assert.Equal(393d, result.LogicalWidth);
        Assert.Equal(852d, result.LogicalHeight);
        Assert.Equal(0.7480916030534351, result.CaptureScale);
        Assert.Equal(3, result.NativeScale);
        Assert.True(result.ResolutionClamped == true);
        await serverTask;
    }

    [Fact]
    public async Task ScreenshotResultAsync_WindowNotVisible_ReturnsRetryableFailureWithSuggestions()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        const string body = """
        {
          "success": false,
          "error": "Failed to capture the app window because it is hidden or minimized.",
          "reason": "window-not-visible",
          "details": {
            "retryable": true,
            "suggestions": [
              "Restore the app window without activating it, then retry.",
              "Ensure Screen & System Audio Recording permission is granted if native window capture is required."
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
        Assert.Equal("window-not-visible", result.Reason);
        Assert.True(result.Retryable);
        Assert.NotNull(result.Error);
        Assert.Contains("hidden or minimized", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Suggestions);
        Assert.Equal(2, result.Suggestions!.Count);
        Assert.Contains(result.Suggestions, s => s.Contains("without activating", StringComparison.OrdinalIgnoreCase));

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
        { "success": false, "error": "Failed to capture screenshot", "reason": "window-not-visible", "details": { "retryable": true } }
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

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
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
