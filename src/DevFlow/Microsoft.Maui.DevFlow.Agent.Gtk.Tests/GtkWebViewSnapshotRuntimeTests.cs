using System.Runtime.InteropServices;
using Microsoft.Maui.DevFlow.Blazor.Gtk;
using Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;

namespace Microsoft.Maui.DevFlow.Agent.Gtk.Tests;

[Collection("GTK snapshot runtime")]
public class GtkWebViewSnapshotRuntimeTests
{
    [GtkSnapshotFact]
    public void NativeSnapshot_CapturesRenderedWebContentAsPng_NotTheWrapperRenderNode()
    {
        global::Gtk.Module.Initialize();
        global::Gtk.Functions.Init();
        GtkBlazorWebView.InitializeWebKit();
        using var window = global::Gtk.Window.New();
        using var wrapper = global::Gtk.Box.New(global::Gtk.Orientation.Vertical, 0);
        using var view = new WebKit.WebView();
        view.SetHexpand(true);
        view.SetVexpand(true);
        wrapper.Append(view);
        window.SetDefaultSize(320, 240);
        window.SetChild(wrapper);
        var loaded = false;
        void OnLoadChanged(WebKit.WebView sender, WebKit.WebView.LoadChangedSignalArgs args)
            => loaded |= args.LoadEvent == WebKit.LoadEvent.Finished;
        view.OnLoadChanged += OnLoadChanged;
        try
        {
            Assert.Throws<InvalidOperationException>(() => { _ = WebKitSnapshot.CaptureAsync(view, CancellationToken.None); });
            view.LoadHtml("<html><style>html,body{margin:0;background:#e04030;width:100%;height:100%;}</style><body>Native WebKit snapshot</body></html>", null);
            window.Present();
            PumpUntil(() => loaded && view.GetMapped() && view.GetWidth() > 0 && view.GetHeight() > 0);

            var capture = WebKitSnapshot.CaptureAsync(view, CancellationToken.None);
            PumpUntil(() => capture.IsCompleted);
            var png = capture.GetAwaiter().GetResult();
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8).ToArray());
            using var bytes = GLib.Bytes.New(png);
            using var image = Gdk.Texture.NewFromBytes(bytes);
            var width = image.GetWidth();
            var height = image.GetHeight();
            Assert.True(width >= view.GetWidth() && height >= view.GetHeight());
            var pixels = new byte[checked(width * height * 4)];
            DownloadTexture(image.Handle.DangerousGetHandle(), pixels, (nuint)(width * 4));
            var offset = (height / 2 * width + width / 2) * 4;
            // GDK_MEMORY_DEFAULT is premultiplied BGRA on little-endian hosts.
            Assert.Equal(BitConverter.IsLittleEndian
                ? new byte[] { 0x30, 0x40, 0xe0, 0xff }
                : new byte[] { 0xff, 0xe0, 0x40, 0x30 }, pixels.AsSpan(offset, 4).ToArray());
        }
        finally
        {
            view.OnLoadChanged -= OnLoadChanged;
            window.SetChild(null);
            wrapper.Remove(view);
            window.Destroy();
        }
    }

    [GtkSnapshotFact]
    public void Bridge_DelayedDocumentAndReplacement_ReinitializesFromNativeLoadSignals()
    {
        global::Gtk.Module.Initialize();
        global::Gtk.Functions.Init();
        GtkBlazorWebView.InitializeWebKit();
        using var window = global::Gtk.Window.New();
        using var view = new WebKit.WebView();
        using var service = new GtkBlazorWebViewDebugService();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new GtkRuntimeSynchronizationContext());
        try
        {
            window.SetDefaultSize(320, 240);
            window.SetChild(view);
            window.Present();
            var bridge = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, view, "delayed");
            service.AddBridge(bridge);
            var initialize = bridge.InitializeAsync();
            PumpUntil(() => initialize.IsCompleted);
            initialize.GetAwaiter().GetResult();
            Assert.False(bridge.IsReady);

            view.LoadHtml("<html><title>Late document</title><body>Late host page</body></html>", null);
            PumpUntil(() => bridge.IsReady);
            AssertDocumentTitle(bridge, "Late document");

            view.LoadHtml("<html><title>Replacement document</title><body>New host page</body></html>", null);
            PumpUntil(() => !bridge.IsReady);
            PumpUntil(() => bridge.IsReady);
            AssertDocumentTitle(bridge, "Replacement document");

            service.RemoveBridge(bridge);
            Assert.False(bridge.IsReady);
        }
        finally
        {
            service.Dispose();
            window.SetChild(null);
            window.Destroy();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [GtkSnapshotFact]
    public void NativeSnapshot_CanceledDuringViewTeardown_CompletesWithoutAnImage()
    {
        global::Gtk.Module.Initialize();
        global::Gtk.Functions.Init();
        GtkBlazorWebView.InitializeWebKit();
        using var window = global::Gtk.Window.New();
        using var view = new WebKit.WebView();
        using var cancellation = new CancellationTokenSource();
        window.SetDefaultSize(320, 240);
        window.SetChild(view);
        try
        {
            view.LoadHtml("<html><body>Cancel native capture</body></html>", null);
            window.Present();
            PumpUntil(() => !view.IsLoading && view.GetMapped() && view.GetWidth() > 0 && view.GetHeight() > 0);

            var capture = WebKitSnapshot.CaptureAsync(view, cancellation.Token);
            cancellation.Cancel();
            window.SetChild(null);
            view.Dispose();
            PumpUntil(() => capture.IsCompleted);

            Assert.ThrowsAny<OperationCanceledException>(() => capture.GetAwaiter().GetResult());
            Assert.False(capture.IsCompletedSuccessfully);
        }
        finally
        {
            window.SetChild(null);
            window.Destroy();
        }
    }

    [GtkSnapshotFact]
    public void Bridge_CommandDuringSlowFullNavigation_DoesNotInitializeTheProvisionalDocument()
    {
        global::Gtk.Module.Initialize();
        global::Gtk.Functions.Init();
        GtkBlazorWebView.InitializeWebKit();
        using var window = global::Gtk.Window.New();
        using var view = new WebKit.WebView();
        using var service = new GtkBlazorWebViewDebugService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        var response = ServeSlowDocumentAsync(server, requested, release, cancellation.Token);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new GtkRuntimeSynchronizationContext());
        try
        {
            window.SetDefaultSize(320, 240);
            window.SetChild(view);
            window.Present();
            var bridge = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, view, "slow");
            service.AddBridge(bridge);
            view.LoadHtml("<html><title>Original document</title><body>Original</body></html>", null);
            PumpUntil(() => bridge.IsReady);

            var port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;
            var navigation = bridge.SendCdpCommandAsync(
                $$$"""{"id":92,"method":"Page.navigate","params":{"url":"http://127.0.0.1:{{{port}}}/"}}""");
            PumpUntil(() => requested.Task.IsCompleted && navigation.IsCompleted);
            using var acknowledgement = System.Text.Json.JsonDocument.Parse(navigation.GetAwaiter().GetResult());
            Assert.True(acknowledgement.RootElement.TryGetProperty("result", out _));
            Assert.False(bridge.IsReady);

            release.TrySetResult();
            PumpUntil(() => response.IsCompleted && bridge.IsReady);
            response.GetAwaiter().GetResult();
            AssertDocumentTitle(bridge, "Slow replacement");
        }
        finally
        {
            release.TrySetResult();
            cancellation.Cancel();
            server.Stop();
            try
            {
                response.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            finally
            {
                service.Dispose();
                window.SetChild(null);
                window.Destroy();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
    }

    static async Task ServeSlowDocumentAsync(
        System.Net.Sockets.TcpListener server, TaskCompletionSource requested,
        TaskCompletionSource release, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await server.AcceptTcpClientAsync(cancellationToken);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken)
                    ?? throw new IOException("The navigation request closed before its headers completed.");
                if (line.Length == 0)
                    break;
            }
            requested.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            var body = System.Text.Encoding.UTF8.GetBytes(
                "<html><title>Slow replacement</title><body>Loaded after command initialization</body></html>");
            var header = System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (System.Net.Sockets.SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    static void AssertDocumentTitle(GtkBlazorWebViewDebugService.GtkWebViewBridge bridge, string expected)
    {
        var evaluation = bridge.SendCdpCommandAsync(
            """{"id":91,"method":"Runtime.evaluate","params":{"expression":"document.title","returnByValue":true}}""");
        PumpUntil(() => evaluation.IsCompleted);
        using var response = System.Text.Json.JsonDocument.Parse(evaluation.GetAwaiter().GetResult());
        Assert.Equal(expected, response.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString());
    }

    sealed class GtkRuntimeSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
            => GLib.Functions.IdleAdd(0, () =>
            {
                callback(state);
                return false;
            });
    }

    [DllImport("libgtk-4.so.1", EntryPoint = "gdk_texture_download", CallingConvention = CallingConvention.Cdecl)]
    static extern void DownloadTexture(nint texture, [Out] byte[] pixels, nuint stride);

    static void PumpUntil(Func<bool> completed)
    {
        var timedOut = false;
        var timeout = GLib.Functions.TimeoutAdd(0, 10_000, () =>
        {
            timedOut = true;
            return false;
        });
        try
        {
            while (!completed() && !timedOut)
                GLib.MainContext.Default().Iteration(true);
            Assert.True(completed(), "The native WebKit operation did not complete before the deadline.");
        }
        finally
        {
            if (!timedOut)
                GLib.Functions.SourceRemove(timeout);
        }
    }
}

internal sealed class GtkSnapshotFactAttribute : FactAttribute
{
    public GtkSnapshotFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("RUN_GTK_RUNTIME_TESTS") != "1")
            Skip = "Requires Linux, GTK 4.12+, WebKitGTK 6.0, a display, and RUN_GTK_RUNTIME_TESTS=1.";
    }
}

[CollectionDefinition("GTK snapshot runtime", DisableParallelization = true)]
public sealed class GtkSnapshotRuntimeCollection;
