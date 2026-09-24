using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// How the agent reads a request body. Bodies are not always text and not always length-prefixed:
/// .NET's own JsonContent sends chunked, and a file upload sends bytes that a UTF-8 round trip
/// would corrupt.
/// </summary>
public class AgentHttpServerBodyTests
{
    [Fact]
    public async Task ChunkedRequestBody_IsReassembledBeforeTheHandlerSeesIt()
    {
        using var server = new AgentHttpServer(GetFreePort());
        string? seen = null;
        server.MapPost("/echo", request =>
        {
            seen = request.Body;
            return Task.FromResult(HttpResponse.Ok("read"));
        });
        server.Start();

        // JsonContent cannot compute its length, so HttpClient frames this as chunked.
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{server.Port}") };
        var response = await SendWithRetryAsync(() => http.PostAsJsonAsync("/echo", new EchoPayload("hello", 42)));

        Assert.True(response.IsSuccessStatusCode);
        Assert.NotNull(seen);

        using var parsed = JsonDocument.Parse(seen!);
        // PostAsJsonAsync serialises with the web defaults, so the names come across camel-cased.
        Assert.Equal("hello", parsed.RootElement.GetProperty("name").GetString());
        Assert.Equal(42, parsed.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ChunkedRequestBody_SurvivesBeingSplitAcrossPackets()
    {
        using var server = new AgentHttpServer(GetFreePort());
        string? seen = null;
        server.MapPost("/echo", request =>
        {
            seen = request.Body;
            return Task.FromResult(HttpResponse.Ok("read"));
        });
        server.Start();

        // Two chunks, written with a pause between them, so the reader has to come back for more.
        var answered = await SendRawAsync(server.Port, write: async stream =>
        {
            await WriteAsciiAsync(stream, "POST /echo HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n");
            await WriteAsciiAsync(stream, "9\r\n{\"a\":1,\"b\r\n");
            await Task.Delay(50);
            await WriteAsciiAsync(stream, "6\r\n\":2}\r\n\r\n0\r\n\r\n");
        });

        Assert.True(answered > 0, "The server closed the connection without answering.");
        Assert.Equal("{\"a\":1,\"b\":2}\r\n", seen);
    }

    [Fact]
    public async Task ChunkedRequestBody_WithAMissingChunkTerminator_IsRefused()
    {
        using var server = new AgentHttpServer(GetFreePort());
        var handlerRan = false;
        server.MapPost("/echo", request =>
        {
            handlerRan = true;
            return Task.FromResult(HttpResponse.Ok("read"));
        }, requiresMutationLease: false);
        server.Start();

        // "5\r\nhelloXX" - the chunk is the right length but what follows it is not CRLF. Skipping
        // two bytes on faith would put everything after this out of step with the length lines.
        var answered = await SendRawAsync(server.Port, write: async stream =>
        {
            await WriteAsciiAsync(stream, "POST /echo HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n");
            await WriteAsciiAsync(stream, "5\r\nhelloXX0\r\n\r\n");
        });

        Assert.False(handlerRan, "A request with malformed chunk framing should never reach a handler.");
        Assert.Equal(0, answered);
    }

    [Fact]
    public async Task ChunkedRequestBody_WithASizeLineThatNeverEnds_IsRefusedWithoutWaitingItOut()
    {
        using var server = new AgentHttpServer(GetFreePort());
        var handlerRan = false;
        server.MapPost("/echo", request =>
        {
            handlerRan = true;
            return Task.FromResult(HttpResponse.Ok("read"));
        }, requiresMutationLease: false);
        server.Start();

        // An extension that never reaches its CRLF. Waiting for the line end buffers every byte of it,
        // and a sender that keeps the bytes coming never trips the idle timeout either.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var answered = await SendRawAsync(server.Port, write: async stream =>
        {
            await WriteAsciiAsync(stream, "POST /echo HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n1;");
            await WriteAsciiAsync(stream, new string('x', 64 * 1024));
        });

        Assert.False(handlerRan);
        Assert.Equal(0, answered);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Refused only after {watch.Elapsed}, which is the idle timeout rather than the line limit.");
    }

    [Fact]
    public async Task ChunkedRequestBody_WhoseFramingOutweighsAnyRealBody_IsRefused()
    {
        using var server = new AgentHttpServer(GetFreePort());
        var handlerRan = false;
        server.MapPost("/echo", request =>
        {
            handlerRan = true;
            return Task.FromResult(HttpResponse.Ok("read"));
        }, requiresMutationLease: false);
        server.Start();

        // Two megabytes on the wire for a two-kilobyte body: every line is legal on its own, and the
        // body ceiling never comes into it.
        var framing = new StringBuilder();
        var extension = new string('x', 1000);
        for (var i = 0; i < 2000; i++)
            framing.Append("1;").Append(extension).Append("\r\nA\r\n");
        framing.Append("0\r\n\r\n");

        var answered = await SendRawAsync(server.Port, write: async stream =>
        {
            await WriteAsciiAsync(stream, "POST /echo HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n");
            await WriteAsciiAsync(stream, framing.ToString());
        });

        Assert.False(handlerRan, "A body that is almost all framing should never reach a handler.");
        Assert.Equal(0, answered);
    }

    [Fact]
    public async Task ChunkedRequestBody_OfManyTinyChunks_StillArrivesWhole()
    {
        using var server = new AgentHttpServer(GetFreePort());
        string? seen = null;
        server.MapPost("/echo", request =>
        {
            seen = request.Body;
            return Task.FromResult(HttpResponse.Ok("read"));
        }, requiresMutationLease: false);
        server.Start();

        // Ten thousand chunks, so the parsed bytes are dropped many times over along the way.
        var chunks = new StringBuilder();
        for (var i = 0; i < 10_000; i++)
            chunks.Append("4\r\nabcd\r\n");
        chunks.Append("0\r\n\r\n");

        var answered = await SendRawAsync(server.Port, write: async stream =>
        {
            await WriteAsciiAsync(stream, "POST /echo HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n");
            await WriteAsciiAsync(stream, chunks.ToString());
        });

        Assert.True(answered > 0, "The server closed the connection without answering.");
        Assert.Equal(40_000, seen?.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("abcd", 10_000)), seen);
    }

    [Fact]
    public async Task BinaryRequestBody_IsNotMangledByTextDecoding()
    {
        using var server = new AgentHttpServer(GetFreePort());
        byte[]? seen = null;
        server.MapPut("/blob", request =>
        {
            seen = request.BodyBytes;
            return Task.FromResult(HttpResponse.Ok("read"));
        });
        server.Start();

        var payload = new byte[256];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)i;

        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{server.Port}") };
        using var content = new ByteArrayContent(payload);
        var response = await SendWithRetryAsync(() => http.PutAsync("/blob", content));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(payload, seen);
    }

    private sealed record EchoPayload(string Name, int Count);

    private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> send)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await send(); }
            catch (HttpRequestException) when (attempt < 9) { await Task.Delay(100); }
        }
    }

    /// <summary>
    /// Writes a request byte for byte and answers with how many bytes came back.
    /// </summary>
    /// <remarks>
    /// The count is the point: a request the server accepted is answered, and one it refused as
    /// malformed has its connection closed with nothing written. Both are outcomes worth asserting
    /// on, so this reports rather than assumes.
    /// </remarks>
    private static async Task<int> SendRawAsync(int port, Func<NetworkStream, Task> write)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await using var stream = client.GetStream();

                try
                {
                    await write(stream);

                    var buffer = new byte[1024];
                    return await stream.ReadAsync(buffer);
                }
                catch (IOException)
                {
                    // The server hung up while the request was still being written - which is how a
                    // refusal lands when there is more request than the server was willing to read.
                    return 0;
                }
            }
            catch (SocketException) when (attempt < 9) { await Task.Delay(100); }
        }
    }

    private static Task WriteAsciiAsync(NetworkStream stream, string text)
        => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
