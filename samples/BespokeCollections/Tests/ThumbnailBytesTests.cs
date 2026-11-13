using System.Net;
using BespokeCollections.Core;
using BespokeCollections.Fixture;
using Xunit;

namespace BespokeCollections.Tests;

public sealed class ThumbnailBytesTests
{
    [Fact]
    public async Task Get_ConcurrentConsumers_SharesRequestAndKeepsOtherDemand()
    {
        using var handler = new ImageHandler();
        using var http = new HttpClient(handler);
        using var cache = new ThumbnailBytes(new(http, new Uri("http://fixture/")));
        using var consumer = new CancellationTokenSource();
        var photo = FixtureCatalog.Item(0, "0");
        var cancelled = cache.GetAsync(photo, 160, consumer.Token);
        var active = cache.GetAsync(photo, 160, CancellationToken.None);
        consumer.Cancel();
        handler.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(300_000, (await active).Length);
        Assert.Equal(1, handler.Requests);
        Assert.Same(await active, await cache.GetAsync(photo, 160, CancellationToken.None));
    }

    [Fact]
    public async Task Get_ManyImages_RespectsEncodedByteBudget()
    {
        using var handler = new ImageHandler();
        handler.Release();
        using var http = new HttpClient(handler);
        using var cache = new ThumbnailBytes(new(http, new Uri("http://fixture/")), budget: 600_000);
        for (var index = 0; index < 8; index++)
            await cache.GetAsync(FixtureCatalog.Item(index, "0"), 160, CancellationToken.None);
        Assert.InRange(cache.Snapshot.Bytes, 0, 600_000);
        Assert.Equal(2, cache.Snapshot.Entries);
    }

    [Fact]
    public async Task Get_AllConsumersCancel_NewConsumerCanRequestSameKey()
    {
        using var handler = new ImageHandler();
        using var http = new HttpClient(handler);
        using var cache = new ThumbnailBytes(new(http, new Uri("http://fixture/")));
        using var consumer = new CancellationTokenSource();
        var photo = FixtureCatalog.Item(0, "0");
        var departed = cache.GetAsync(photo, 160, consumer.Token);
        consumer.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => departed);
        var replacement = cache.GetAsync(photo, 160, CancellationToken.None);
        handler.Release();
        Assert.Equal(300_000, (await replacement).Length);
    }

    sealed class ImageHandler : HttpMessageHandler
    {
        readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests;
        public void Release() => _release.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            await _release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[300_000]) };
        }
    }
}
