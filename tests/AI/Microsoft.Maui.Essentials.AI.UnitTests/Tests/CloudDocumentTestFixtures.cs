using System.Net;
using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

internal static class CloudDocumentTestFixtures
{
    internal static readonly Uri OcrEndpoint = new("https://example.invalid/resource/providers/mistral/azure/ocr");

    internal static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new BoundedDocumentResponseHandler { InnerHandler = new Handler(respond) }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static HttpClient Http(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) =>
        Http((request, token) => Task.FromResult(respond(request, token)));

    internal static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    internal static DocumentExtractionClient Mistral(HttpClient http, string deployment = "deployment") =>
        new CloudDocumentExtractionPolicy(new MistralDocumentExtractionClient(
            new() { OcrEndpoint = OcrEndpoint, DefaultModel = deployment, IncludeImages = true }, "test-key", http),
            TimeSpan.FromMinutes(5));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            respond(request, token);
    }

    internal sealed class TrackingContent(string value) : StringContent(value)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    internal sealed class RepeatingStream(long length) : Stream
    {
        private long _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(_remaining, buffer.Length);
            buffer.Span[..count].Fill(1);
            _remaining -= count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
