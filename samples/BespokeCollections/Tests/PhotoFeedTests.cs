using System.Net;
using System.Text.Json;
using BespokeCollections.Core;
using BespokeCollections.Fixture;
using Xunit;

namespace BespokeCollections.Tests;

public sealed class PhotoFeedTests
{
    [Fact]
    public async Task LoadNext_ConcurrentDemand_SharesOneRequest()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        using var feed = Feed(http);
        var first = feed.LoadNextAsync();
        var second = feed.LoadNextAsync();
        Assert.Same(first, second);
        handler.Release();
        await first;
        Assert.Equal(1, handler.Requests);
        Assert.Equal(50, feed.State.Items.Count);
        Assert.False(feed.State.IsLoading);
    }

    [Fact]
    public async Task Refresh_OldQueryCompletesLate_CannotPublishOldEpoch()
    {
        using var handler = new FixtureHandler(ignoreCancellation: true);
        using var http = new HttpClient(handler);
        using var feed = Feed(http);
        var old = feed.RefreshAsync("0");
        var current = feed.RefreshAsync("1");
        handler.Release();
        await current;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        Assert.Equal("1", feed.State.Query);
        Assert.All(feed.State.Items, item => Assert.True(item.Id > FixtureCatalog.Count));
        Assert.Equal(2, feed.State.Epoch);
    }

    [Fact]
    public async Task LoadNext_PageWindow_PrunesResidentData()
    {
        using var handler = new FixtureHandler();
        handler.Release();
        using var http = new HttpClient(handler);
        using var feed = Feed(http, maximum: 100);
        await feed.RefreshAsync();
        await feed.LoadNextAsync();
        await feed.LoadNextAsync();
        Assert.Equal(100, feed.State.Items.Count);
        Assert.Equal(51, feed.State.Items[0].Id);
        Assert.Equal(50, feed.State.EvictedItems);
    }

    [Fact]
    public async Task LoadNext_HttpFailure_IsNotEmptySuccessAndCanRetry()
    {
        using var handler = new FixtureHandler { Fail = true };
        handler.Release();
        using var http = new HttpClient(handler);
        using var feed = Feed(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => feed.RefreshAsync());
        Assert.NotNull(feed.State.Error);
        Assert.False(feed.State.IsLoading);
        Assert.True(feed.State.HasMore);
        handler.Fail = false;
        await feed.LoadNextAsync();
        Assert.Null(feed.State.Error);
        Assert.Equal(50, feed.State.Items.Count);
    }

    [Fact]
    public async Task Dispose_LateCompletion_DoesNotNotifyOrReviveSource()
    {
        using var handler = new FixtureHandler(ignoreCancellation: true);
        using var http = new HttpClient(handler);
        var feed = Feed(http);
        var changed = 0;
        feed.Changed += (_, _) => changed++;
        var loading = feed.RefreshAsync();
        feed.Dispose();
        var atDisconnect = changed;
        handler.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.Equal(atDisconnect, changed);
        Assert.Empty(feed.State.Items);
    }

    static PhotoFeed Feed(HttpClient http, int maximum = 500) =>
        new(new(http, new Uri("http://fixture/")), action => { action(); return Task.CompletedTask; }, maximum);

    sealed class FixtureHandler(bool ignoreCancellation = false) : HttpMessageHandler
    {
        readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests;
        public bool Fail;
        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (ignoreCancellation)
                await _release.Task;
            else
                await _release.Task.WaitAsync(cancellationToken);
            if (Fail)
                return new(HttpStatusCode.ServiceUnavailable);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var page = FixtureCatalog.Page(query["query"].ToString(), query["cursor"].ToString(), 50);
            return new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(page, PhotoJsonContext.Default.PhotoPage))
            };
        }
    }
}
