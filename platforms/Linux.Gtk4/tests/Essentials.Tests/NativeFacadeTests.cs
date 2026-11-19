using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
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
                    var nativeDisplay = Gdk.Display.GetDefault();
                    Assert.NotNull(nativeDisplay);
                    var monitors = nativeDisplay.GetMonitors();
                    Assert.True(monitors.GetNItems() > 0);
                    var monitor = Assert.IsType<Gdk.Monitor>(monitors.GetObject(0), exactMatch: false);
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
}

public sealed class NativeGtkFactAttribute : FactAttribute
{
    public NativeGtkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ESSENTIALS_NATIVE_GTK") != "1")
            Skip = "Requires a real GTK display: run with ESSENTIALS_NATIVE_GTK=1 under WSLg or xvfb-run.";
    }
}
