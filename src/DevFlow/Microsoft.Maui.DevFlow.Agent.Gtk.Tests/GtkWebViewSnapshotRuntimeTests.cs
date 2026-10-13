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
