using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Accessibility;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;
using Microsoft.Maui.Storage;

namespace Essentials.Tests;

public class NativeFacadeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [NativeGtkFact]
    public void ActivatedGtkApplication_StaticFacades_UseLivePlatformServices()
    {
        StaticFacadeTests.Run(() =>
        {
            using var gtkApp = Gtk.Application.New(null, Gio.ApplicationFlags.NonUnique);
            Exception? error = null;
            var activated = false;
            gtkApp.OnActivate += (_, _) =>
            {
                activated = true;
                try
                {
                    var screenReader = new LinuxSemanticScreenReader();
                    screenReader.Announce(" ");
                    Assert.Null(gtkApp.GetActiveWindow());
                    var announcementError = Assert.Throws<InvalidOperationException>(
                        () => screenReader.Announce("Before window activation"));
                    Assert.Contains("window is required", announcementError.Message);
                    using var nativeDisplay = Gdk.Display.GetDefault();
                    Assert.NotNull(nativeDisplay);
                    using var monitors = nativeDisplay.GetMonitors();
                    Assert.True(monitors.GetNItems() > 0);
                    using var monitorObject = monitors.GetObject(0);
                    var monitor = Assert.IsType<Gdk.Monitor>(monitorObject, exactMatch: false);
                    monitor.GetGeometry(out var geometry);

                    using var app = MauiApp.CreateBuilder().AddLinuxGtk4Essentials().Build();
                    Assert.Equal(app.Services.GetRequiredService<IFileSystem>().AppDataDirectory, FileSystem.AppDataDirectory);
                    Assert.Equal(DeviceIdiom.Desktop, DeviceInfo.Idiom);
                    var display = DeviceDisplay.MainDisplayInfo;
                    Assert.Equal(geometry.Width * monitor.GetScaleFactor(), display.Width);
                    Assert.Equal(geometry.Height * monitor.GetScaleFactor(), display.Height);
                    output.WriteLine($"GTK activated; FileSystem={FileSystem.AppDataDirectory}; DeviceInfo={DeviceInfo.Idiom}; Display={display}");
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    gtkApp.Quit();
                }
            };
            Assert.Equal(0, gtkApp.Run([]));
            Assert.True(activated);
            if (error is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        });
    }

    [NativeGtkFact]
    public void PresentedGtkWindow_ScreenshotReturnsReusablePngResult()
    {
        var criticals = new ConcurrentQueue<string>();
        NativeLogHandler logHandler = (_, _, message, _) =>
            criticals.Enqueue(Marshal.PtrToStringUTF8(message) ?? "Unknown GObject critical");
        var logId = SetLogHandler("GLib-GObject", 1 << 3, logHandler, 0);
        using var gtkApp = Gtk.Application.New(null, Gio.ApplicationFlags.NonUnique);
        Exception? error = null;
        var executed = false;
        gtkApp.OnActivate += (_, _) =>
        {
            var window = Gtk.ApplicationWindow.New(gtkApp);
            var label = Gtk.Label.New("GTK screenshot");
            window.SetDefaultSize(320, 180);
            window.SetChild(label);
            window.Present();

            _ = CaptureFromWorkerAsync();

            async Task CaptureFromWorkerAsync()
            {
                executed = true;
                try
                {
                    await WaitForAllocationAsync(window, TimeSpan.FromSeconds(5));
                    for (var capture = 0; capture < 8; capture++)
                    {
                        var result = await Task.Run(
                            async () => await new LinuxScreenshot().CaptureAsync());
                        Assert.True(result.Width > 0);
                        Assert.True(result.Height > 0);

                        using var first = await result.OpenReadAsync();
                        using var second = await result.OpenReadAsync();
                        var png = new byte[first.Length];
                        await first.ReadExactlyAsync(png);
                        Assert.True(png.Length > 24);
                        Assert.Equal(first.Length, second.Length);
                        Assert.Equal(result.Width, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
                        Assert.Equal(result.Height, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
                        await Task.Run(() =>
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        });
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    GLib.Functions.IdleAdd(0, () =>
                    {
                        window.SetChild(null);
                        window.Close();
                        label.Dispose();
                        window.Dispose();
                        gtkApp.Quit();
                        return false;
                    });
                }
            }
        };

        try
        {
            Assert.Equal(0, gtkApp.Run([]));
        }
        finally
        {
            RemoveLogHandler("GLib-GObject", logId);
            GC.KeepAlive(logHandler);
        }
        Assert.True(executed);
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        Assert.Empty(criticals);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void NativeLogHandler(nint domain, uint level, nint message, nint userData);

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_log_set_handler", CallingConvention = CallingConvention.Cdecl)]
    static extern uint SetLogHandler(string domain, uint levels, NativeLogHandler handler, nint userData);

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_log_remove_handler", CallingConvention = CallingConvention.Cdecl)]
    static extern void RemoveLogHandler(string domain, uint handlerId);

    static Task WaitForAllocationAsync(Gtk.Widget widget, TimeSpan timeout)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = Stopwatch.StartNew();
        GLib.Functions.TimeoutAdd(0, 10, () =>
        {
            if (widget.GetWidth() > 0 && widget.GetHeight() > 0)
            {
                completion.TrySetResult();
                return false;
            }

            if (timer.Elapsed >= timeout)
            {
                completion.TrySetException(new TimeoutException(
                    $"GTK widget did not receive a non-zero allocation within {timeout}."));
                return false;
            }

            return true;
        });
        return completion.Task;
    }
}

public sealed class NativeGtkFactAttribute : FactAttribute
{
    public NativeGtkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ESSENTIALS_NATIVE_GTK") != "1")
            Skip = "Requires a real GTK display: run with ESSENTIALS_NATIVE_GTK=1 under WSLg or xvfb-run.";
    }
}
