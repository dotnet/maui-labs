using System.Diagnostics;
using System.Text.Json;
using BespokeCollections.Core;
using Microsoft.Extensions.Logging;

namespace BespokeCollections.App;

public sealed class MainPage : ContentPage, IDisposable
{
    readonly ILogger _logger;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Entry _endpoint = new()
    {
        Text = "http://127.0.0.1:18550/",
        AutomationId = "fixture-url",
        FontSize = 12,
        TextColor = Colors.White,
        BackgroundColor = Color.FromArgb("#263A52"),
        MinimumHeightRequest = 44
    };
    readonly Label _status = new() { AutomationId = "feed-status", FontSize = 12, TextColor = Colors.White };
    readonly Grid _host = new();
    PhotoFeed? _feed;
    ThumbnailBytes? _thumbnails;
    PhotoMasonryView? _view;
    bool _disposed;
    bool _proofRunning;
    bool _automaticProofStarted;
    int _query;

    public MainPage(ILogger<MainPage> logger)
    {
        _logger = logger;
        Title = "Photo masonry";
        BackgroundColor = Color.FromArgb("#10223D");
        var connect = Button("Connect", "connect-fixture", ConnectAsync);
        var more = Button("More", "load-more", () => _feed?.LoadNextAsync() ?? Task.CompletedTask);
        var refresh = Button("Refresh", "refresh-feed", () => _feed?.RefreshAsync((_query++ % 2).ToString()) ?? Task.CompletedTask);
        var proof = Button("Run proof", "run-proof", RunProofAsync);
        var scroll = Button("Scroll", "scroll-masonry", () => { _view?.ScrollToOffset(2200); return Task.CompletedTask; });
        var controls = new HorizontalStackLayout { Spacing = 4, Children = { connect, more, refresh, scroll, proof } };
        var grid = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) } };
        grid.Add(_endpoint, 0, 0);
        grid.Add(controls, 0, 1);
        grid.Add(_status, 0, 2);
        grid.Add(_host, 0, 3);
        Content = grid;
        AutomationId = "masonry-page";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!PrototypeLaunch.RunProof || _automaticProofStarted)
            return;
        _automaticProofStarted = true;
        try { await RunProofAsync(); }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Automatic native proof failed");
            _status.Text = $"ERROR: {exception.Message}";
        }
    }

    Button Button(string text, string id, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            AutomationId = id,
            FontSize = 11,
            Padding = new Thickness(8, 4),
            MinimumWidthRequest = 0,
            MinimumHeightRequest = 44,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#263A52")
        };
        button.Clicked += async (_, _) =>
        {
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Prototype action {Action} failed", id);
                _status.Text = $"ERROR: {exception.Message}";
            }
        };
        return button;
    }

    Task PublishAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Dispatcher.Dispatch(() =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
            completion.TrySetException(new InvalidOperationException("The owning dispatcher rejected publication."));
        return completion.Task;
    }

    async Task ConnectAsync()
    {
        if (!Uri.TryCreate(_endpoint.Text, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Enter the fixture's absolute HTTP URL.");
        _view?.Handler?.DisconnectHandler();
        _host.Children.Clear();
        _feed?.Dispose();
        _thumbnails?.Dispose();
        var repository = new PhotoRepository(_http, url);
        _feed = new(repository, PublishAsync);
        _thumbnails = new(repository);
        _view = new(_feed, _thumbnails, _logger) { AutomationId = "photo-masonry" };
        _host.Add(_view);
        _feed.Changed += (_, _) =>
        {
            var state = _feed.State;
            _status.Text = state.Error is not null ? $"ERROR: {state.Error}" :
                $"{state.Items.Count} retained | page {state.PagesLoaded} | epoch {state.Epoch} | " +
                (state.IsLoading ? "loading" : state.HasMore ? "ready" : "end");
        };
        await _feed.RefreshAsync();
    }

    async Task WaitAsync(Func<bool> ready, string description)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(15))
                throw new TimeoutException($"Native proof timed out: {description}");
            await Task.Delay(30);
        }
    }

    async Task RunProofAsync()
    {
        if (_proofRunning)
            throw new InvalidOperationException("A native proof is already running.");
        var reportPath = GetReportPath();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var scrollWorkload = PrototypeLaunch.CaptureScrollWorkload;
        _proofRunning = true;
        try
        {
            File.Delete(reportPath);
            if (_view is null || _feed is null)
                await ConnectAsync();
            var view = _view!;
            var feed = _feed!;
            view.AutomaticLoadingEnabled = false;
            await feed.RefreshAsync("0");
            while (feed.State.Items.Count < 200)
                await feed.LoadNextAsync();
            var assertions = new List<string>();
            void Assert(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException("Native assertion failed: " + name);
                assertions.Add(name);
            }
            await WaitAsync(() => view.Inspect().Cells.Length > 0, "realized native cells");
            var before = view.Inspect();
            Assert(before.Connected && before.Host.Contains(before.Platform == "ios" ? "UICollectionView" : "RecyclerView", StringComparison.Ordinal), "real native collection host");
            Assert(before.Realized < 50 && before.ItemCount >= 200, "bounded native realization");
            bool GeometryReady()
            {
                var native = view.Inspect();
                if (native.UpdatePending || native.Cells.Length == 0 || native.Viewport.Width <= 24)
                    return false;
                var geometry = MasonryGeometry.Create(feed.State.Items, native.Viewport.Width,
                    Math.Clamp((int)(native.Viewport.Width / 160), 2, 4));
                var positions = feed.State.Items.Select((item, index) => (item, index)).ToDictionary(pair => pair.item.Id);
                if (!native.Cells.All(cell =>
                {
                    if (!positions.TryGetValue(cell.Key, out var pair) || pair.item.Caption != cell.Caption) return false;
                    var expected = geometry[pair.index];
                    return Math.Abs(expected.X - cell.Frame.X) < 1 && Math.Abs(expected.Y - cell.Frame.Y) < 1 &&
                        Math.Abs(expected.Width - cell.Frame.Width) < 1 && Math.Abs(expected.Height - cell.Frame.Height) < 1;
                })) return false;
                var realizedKeys = native.Cells.Select(cell => cell.Key).ToHashSet();
                return geometry.Intersecting(native.Viewport, out _)
                    .All(index => realizedKeys.Contains(feed.State.Items[index].Id));
            }
            void AssertGeometry() => Assert(GeometryReady(), "native frames and captions match metadata-sized shortest-column geometry");
            async Task ScrollAndWaitAsync(double target)
            {
                view.ScrollToOffset(target);
                await WaitAsync(() => Math.Abs(view.Inspect().Offset - target) < 2 && GeometryReady(),
                    "native offset, frames and visible keys");
            }
            await WaitAsync(GeometryReady, "initial native geometry");
            AssertGeometry();
            foreach (var target in new[] { 0d, 900d, 2200d, 4000d, 2200d, 0d })
                await ScrollAndWaitAsync(target);
            var warm = view.Inspect().Created;
            foreach (var target in new[] { 900d, 2200d, 4000d, 0d })
                await ScrollAndWaitAsync(target);
            var repeat = view.Inspect().Created;
            Assert(repeat <= warm + 8, $"native cells reused on repeat traversal (warm {warm}, repeat {repeat})");
            AssertGeometry();
            if (scrollWorkload)
            {
                Console.WriteLine($"MASONRY_SCROLL_BEGIN {DateTimeOffset.UtcNow:O}");
#if ANDROID
                if (OperatingSystem.IsAndroidVersionAtLeast(29))
                    Android.OS.Trace.BeginAsyncSection("MasonryScrollWorkload", 1);
#endif
                try
                {
                    for (var pass = 0; pass < 4; pass++)
                        for (var step = 0; step <= 120; step++)
                        {
                            var offset = pass % 2 == 0 ? step * 50d : (120 - step) * 50d;
                            view.ScrollToOffset(offset);
                            await Task.Delay(16);
                        }
                }
                finally
                {
#if ANDROID
                    if (OperatingSystem.IsAndroidVersionAtLeast(29))
                        Android.OS.Trace.EndAsyncSection("MasonryScrollWorkload", 1);
#endif
                }
                Console.WriteLine($"MASONRY_SCROLL_END {DateTimeOffset.UtcNow:O}");
                await ScrollAndWaitAsync(0);
            }
            await WaitAsync(() => view.Inspect().Cells.Any(cell => cell.ImageKey > 0), "delayed thumbnail publication");
            Assert(view.Inspect().Cells.All(cell => cell.ImageKey == 0 || cell.ImageKey == cell.Key), "thumbnail belongs to current binding");
            using (var scripted = await _http.PostAsync(new Uri(new Uri(_endpoint.Text!), "control/fail-next"), null))
                scripted.EnsureSuccessStatusCode();
            var beforeFailure = feed.State;
            try
            {
                await feed.LoadNextAsync();
                throw new InvalidOperationException("The scripted metadata failure was not surfaced.");
            }
            catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Assert(feed.State.Error is not null && !feed.State.IsLoading &&
                    _status.Text.StartsWith("ERROR:", StringComparison.Ordinal), "metadata failure is visible and loading stops");
            }
            Assert(ReferenceEquals(beforeFailure.Items, feed.State.Items) && GeometryReady(),
                "failed page preserves retained data and native geometry");
            await feed.LoadNextAsync();
            await WaitAsync(GeometryReady, "retry page geometry");
            Assert(feed.State.Error is null && feed.State.PagesLoaded == beforeFailure.PagesLoaded + 1,
                "failed cursor page retries successfully");
            await feed.RefreshAsync("1");
            await WaitAsync(() => view.Inspect().Cells.Length > 0 && view.Inspect().Cells.All(cell => cell.Key > 100_000), "query epoch replacement");
            await WaitAsync(() => view.Inspect().Cells.Any(cell => cell.ImageKey > 100_000), "new query thumbnail publication");
            Assert(view.Inspect().Cells.All(cell => cell.ImageKey == 0 || cell.ImageKey == cell.Key), "old query images cannot leak");
            Assert(view.Inspect().Cells.All(cell => cell.Epoch == feed.State.Epoch), "native cells use the current source epoch");
            await WaitAsync(GeometryReady, "replacement query geometry");
            AssertGeometry();
            while (feed.State.PagesLoaded < 10)
                await feed.LoadNextAsync();
            await WaitAsync(() => view.Inspect().ItemCount == 500, "bounded resident window");
            var anchorGeometry = MasonryGeometry.Create(feed.State.Items, view.Inspect().Viewport.Width,
                Math.Clamp((int)(view.Inspect().Viewport.Width / 160), 2, 4));
            var anchorTarget = anchorGeometry[120].Y;
            await ScrollAndWaitAsync(anchorTarget);
            var anchorBefore = view.Inspect();
            var anchor = anchorBefore.Cells.OrderBy(cell => cell.Frame.Y).First();
            var relativeOffset = anchor.Frame.Y - anchorBefore.Offset;
            await feed.LoadNextAsync();
            await WaitAsync(() => GeometryReady() && view.Inspect().Cells.Any(cell => cell.Key == anchor.Key &&
                Math.Abs(cell.Frame.Y - view.Inspect().Offset - relativeOffset) < 1), "keyed anchor after prefix eviction");
            Assert(feed.State.Items.Count == 500 && feed.State.EvictedItems == 50, "metadata window evicts rather than grows");
            Assert(view.Inspect().Cells.Any(cell => cell.Key == anchor.Key &&
                Math.Abs(cell.Frame.Y - view.Inspect().Offset - relativeOffset) < 1), "keyed relative anchor survives window eviction");
            AssertGeometry();
            Assert(view.Inspect().DecodedBytes <= 16 * 1024 * 1024, "decoded thumbnail budget");
            Assert(_thumbnails!.Snapshot.Bytes <= 4 * 1024 * 1024, "encoded cache budget");
            Assert(view.Inspect().ImageErrors == 0, "no thumbnail rendering errors");
            var primaryNative = view.Inspect();
            var primaryState = feed.State;
            var primaryEncodedBytes = _thumbnails.Snapshot.Bytes;
            var oldView = view;
            var oldFeed = feed;
            await ConnectAsync();
            view = _view!;
            feed = _feed!;
            view.AutomaticLoadingEnabled = false;
            await WaitAsync(() => view.Inspect().Cells.Length > 0, "replacement handler realization");
            await WaitAsync(() => view.Inspect().Cells.Any(cell => cell.ImageKey > 0), "replacement handler image preparation");
            await WaitAsync(GeometryReady, "replacement handler geometry");
            Assert(oldView.Counters.DecodedBytes == 0,
                $"old handler releases decoded images at disconnect (remaining {oldView.Counters.DecodedBytes})");
            Assert(view.Inspect().Connected, "replacement source and handler connect");
            AssertGeometry();
            try { await oldFeed.LoadNextAsync(); throw new InvalidOperationException("The disconnected feed accepted a request."); }
            catch (ObjectDisposedException) { assertions.Add("old source rejects work after replacement"); }
            var report = new PrototypeReport("passed", view.Inspect().Platform,
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                assertions.ToArray(), warm, repeat, primaryNative, view.Inspect(), primaryState.PagesLoaded, primaryState.Items.Count,
                primaryState.EvictedItems, primaryEncodedBytes, GC.GetTotalMemory(false),
                "not-qualified: this report checks native behavior, not presented-frame performance",
                runId, startedAt, DateTimeOffset.UtcNow, scrollWorkload);
            var json = JsonSerializer.Serialize(report, PhotoJsonContext.Default.PrototypeReport);
            File.WriteAllText(reportPath, json);
            Console.WriteLine("MASONRY_PROOF " + json);
#if ANDROID
            Android.Util.Log.Info("MasonryProof", json);
#endif
            _logger.LogInformation("MASONRY_PROOF {Report}", json);
            _status.Text = $"PROOF PASSED: {assertions.Count} assertions";
        }
        catch (Exception exception)
        {
            var failedNative = _view?.Handler is IPhotoMasonryNative native ? native.Inspect() : null;
            var json = JsonSerializer.Serialize(new PrototypeFailure("failed", exception.Message, failedNative,
                runId, startedAt, DateTimeOffset.UtcNow, scrollWorkload),
                PhotoJsonContext.Default.PrototypeFailure);
            File.WriteAllText(reportPath, json);
            Console.WriteLine("MASONRY_PROOF " + json);
#if ANDROID
            Android.Util.Log.Error("MasonryProof", json);
#endif
            throw;
        }
        finally
        {
            if (_view is not null) _view.AutomaticLoadingEnabled = true;
            _proofRunning = false;
        }
    }

    static string GetReportPath()
    {
#if ANDROID
        var directory = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
            ?? throw new IOException("The app's external report directory is unavailable.");
#else
        var directory = FileSystem.AppDataDirectory;
#endif
        return Path.Combine(directory, "native-proof.json");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view?.Handler?.DisconnectHandler();
        _feed?.Dispose();
        _thumbnails?.Dispose();
        _http.Dispose();
    }
}
