using System.Runtime.InteropServices;

namespace Microsoft.Maui.DevFlow.Blazor.Gtk;

internal static class WebKitSnapshot
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void AsyncReadyCallback(nint source, nint result, nint userData);

    static readonly AsyncReadyCallback SnapshotReady = OnSnapshotReady;

    // GirCore 0.7 exposes get_snapshot_finish but not the asynchronous start
    // method on WebView. Use WebKitGTK 6's supported C API, which returns a
    // GdkTexture (not the Cairo surface used by the older GTK 3 API).
    internal static Task<byte[]> CaptureAsync(global::WebKit.WebView view, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!view.GetMapped() || view.GetWidth() <= 0 || view.GetHeight() <= 0)
            throw new InvalidOperationException("The WebKit WebView must be mapped with a nonzero viewport before capture.");

        var request = new SnapshotRequest(view, cancellationToken);
        var handle = GCHandle.Alloc(request);
        try
        {
            Native.GetSnapshot(view.Handle.DangerousGetHandle(),
                global::WebKit.SnapshotRegion.Visible, global::WebKit.SnapshotOptions.None,
                request.Cancellable.Handle.DangerousGetHandle(), SnapshotReady, GCHandle.ToIntPtr(handle));
        }
        catch
        {
            handle.Free();
            request.Dispose();
            throw;
        }
        return request.Completion.Task;
    }

    static void OnSnapshotReady(nint source, nint result, nint userData)
    {
        var handle = GCHandle.FromIntPtr(userData);
        var request = (SnapshotRequest)handle.Target!;
        nint texture = 0;
        nint png = 0;
        nint error = 0;
        try
        {
            texture = Native.GetSnapshotFinish(source, result, ref error);
            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
                return;
            }
            if (error != 0)
            {
                var nativeError = Marshal.PtrToStructure<NativeError>(error);
                throw new InvalidOperationException($"WebKit snapshot failed: {Marshal.PtrToStringUTF8(nativeError.Message)}");
            }
            if (texture == 0)
                throw new InvalidOperationException("WebKit returned no snapshot texture.");

            png = Native.SaveToPngBytes(texture);
            if (png == 0)
                throw new InvalidOperationException("WebKit snapshot could not be encoded as PNG.");
            var data = Native.BytesGetData(png, out var size);
            if (data == 0 || size == 0)
                throw new InvalidOperationException("WebKit snapshot returned empty PNG data.");
            var bytes = new byte[checked((int)size)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            request.Completion.TrySetResult(bytes);
        }
        catch (Exception ex)
        {
            request.Completion.TrySetException(ex);
        }
        finally
        {
            if (error != 0)
                Native.ErrorFree(error);
            if (png != 0)
                Native.BytesUnref(png);
            if (texture != 0)
                Native.ObjectUnref(texture);
            request.Dispose();
            handle.Free();
        }
    }

    sealed class SnapshotRequest : IDisposable
    {
        readonly global::WebKit.WebView _view;
        readonly CancellationTokenRegistration _registration;
        public Gio.Cancellable Cancellable { get; } = Gio.Cancellable.New();
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<byte[]> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SnapshotRequest(global::WebKit.WebView view, CancellationToken cancellationToken)
        {
            _view = view;
            CancellationToken = cancellationToken;
            _registration = cancellationToken.Register(Cancellable.Cancel);
        }

        public void Dispose()
        {
            _registration.Dispose();
            Cancellable.Dispose();
            GC.KeepAlive(_view);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeError
    {
        public uint Domain;
        public int Code;
        public nint Message;
    }

    static class Native
    {
        [DllImport("libwebkitgtk-6.0.so.4", EntryPoint = "webkit_web_view_get_snapshot", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GetSnapshot(nint view, global::WebKit.SnapshotRegion region,
            global::WebKit.SnapshotOptions options, nint cancellable, AsyncReadyCallback callback, nint userData);

        [DllImport("libwebkitgtk-6.0.so.4", EntryPoint = "webkit_web_view_get_snapshot_finish", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint GetSnapshotFinish(nint view, nint result, ref nint error);

        [DllImport("libgtk-4.so.1", EntryPoint = "gdk_texture_save_to_png_bytes", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint SaveToPngBytes(nint texture);

        [DllImport("libglib-2.0.so.0", EntryPoint = "g_bytes_get_data", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint BytesGetData(nint bytes, out nuint size);

        [DllImport("libglib-2.0.so.0", EntryPoint = "g_bytes_unref", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BytesUnref(nint bytes);

        [DllImport("libglib-2.0.so.0", EntryPoint = "g_error_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ErrorFree(nint error);

        [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ObjectUnref(nint obj);
    }
}
