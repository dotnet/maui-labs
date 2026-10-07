using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.DevFlow.Agent.Gtk.Tests;

[Collection("GTK screenshot runtime")]
public class GtkScreenshotRuntimeTests
{
    [GtkScreenshotRuntimeFact]
    public void Screenshot_RepeatedHttpAndNativeCapturesDuringCollection_DetachesPaintables()
    {
        var host = new ScreenshotHost();
        host.Run([]);
        Assert.Null(host.Failure);
        Assert.True(host.Completed);
    }

    private sealed class ScreenshotHost : GtkMauiApplication
    {
        public Exception? Failure { get; private set; }
        public bool Completed { get; private set; }
        protected override bool CreateDesktopEntry => false;
        protected override string ApplicationId => $"org.maui.tests.screenshot.p{Environment.ProcessId}";
        protected override MauiApp CreateMauiApp()
            => MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<ScreenshotApplication>().Build();

        protected override void OnStarted()
            => GLib.Functions.IdleAdd(0, () => { Verify(); return false; });

        private async void Verify()
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var app = (ScreenshotApplication)Application;
            var window = (global::Gtk.Window)app.Windows[0].Handler!.PlatformView!;
            using var agent = new ScreenshotAgent(new AgentOptions
            {
                Port = GetUnusedPort(),
                EnableFileLogging = false,
                EnableNetworkMonitoring = false,
                RequireMutationLease = false
            });
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{agent.Port}"),
                Timeout = TimeSpan.FromSeconds(5)
            };
            using var cancellation = new CancellationTokenSource();
            Task? collector = null;
            uint invalidationSource = 0;
            var collections = 0;
            var invalidations = 0;
            try
            {
                agent.Start(app, app.Dispatcher);
                await client.GetStringAsync("/api/v1/agent/status");
                await Until(() => IsMapped(app.FirstLabel), "initial label allocation");
                collector = Task.Run(async () =>
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        GC.Collect();
                        Interlocked.Increment(ref collections);
                        await Task.Delay(10, cancellation.Token);
                    }
                });
                await AssertNativePng(fromWorker: false);
                await AssertHttpPng("/api/v1/ui/screenshot?scale=native");
                await AssertHttpPng($"/api/v1/ui/screenshot?scale=native&selector=%23{app.CurrentLabel.AutomationId}");
                invalidationSource = GLib.Functions.TimeoutAdd(0, 50, () =>
                {
                    var label = app.CurrentLabel;
                    if (label.Handler?.PlatformView is global::Gtk.Label native && native.GetMapped())
                    {
                        native.SetText($"Native invalidation {++invalidations}");
                        native.QueueResize();
                        native.QueueDraw();
                    }
                    return true;
                });

                using var unallocated = global::Gtk.Box.New(global::Gtk.Orientation.Vertical, 0);
                for (var cycle = 0; cycle < 120; cycle++)
                {
                    if (cycle % 6 == 0)
                    {
                        await app.Shell.GoToAsync(cycle % 12 == 0 ? "//second" : "//first", false);
                        await Until(() => IsMapped(app.CurrentLabel), "Shell page swap allocation");
                    }
                    app.CurrentLabel.Text = $"Capture {cycle}";
                    app.CurrentLayout.Spacing = cycle % 4;
                    await Until(() => IsMapped(app.CurrentLabel), "label allocation");
                    await AssertNativePng();
                    await AssertHttpPng("/api/v1/ui/screenshot?scale=native");
                    await AssertHttpPng($"/api/v1/ui/screenshot?scale=native&selector=%23{app.CurrentLabel.AutomationId}");
                    Assert.Null(await agent.CaptureNativeAsync(unallocated));
                    await Task.Delay(16);
                }

                await Task.Delay(200);
                Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                Assert.True(collections > 0, "Worker collection did not run.");
                Assert.True(invalidations > 0, "Native label invalidation did not run.");
                Completed = true;
                Console.WriteLine($"GTK-SCREENSHOT PASS cycles=120 first-capture=PASS http=242 native=121 early-return=120 caller-retries=0 collections={collections} invalidations={invalidations}");
            }
            catch (Exception ex)
            {
                Failure = ex;
                Console.WriteLine(ex);
            }
            finally
            {
                if (invalidationSource != 0)
                    GLib.Functions.SourceRemove(invalidationSource);
                await cancellation.CancelAsync();
                if (collector != null)
                {
                    try { await collector; }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                }
                try { await agent.StopAsync(); }
                finally { window.Close(); }
            }

            async Task AssertHttpPng(string path)
            {
                using var response = await client.GetAsync(path);
                var data = await response.Content.ReadAsByteArrayAsync();
                Assert.True(response.IsSuccessStatusCode,
                    $"{path}: {(int)response.StatusCode} {System.Text.Encoding.UTF8.GetString(data)}");
                Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
                AssertPng(data);
            }

            async Task AssertNativePng(bool fromWorker = true)
            {
                window.QueueDraw();
                var data = fromWorker
                    ? await Task.Run(() => agent.CaptureNativeAsync(window))
                    : await agent.CaptureNativeAsync(window);
                AssertPng(Assert.IsType<byte[]>(data));
            }
        }

        private static int GetUnusedPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static bool IsMapped(Label label)
            => label.Handler?.PlatformView is global::Gtk.Widget widget
                && widget.GetMapped() && widget.GetWidth() > 0 && widget.GetHeight() > 0;

        private static async Task Until(Func<bool> predicate, string phase)
        {
            var clock = Stopwatch.StartNew();
            while (!predicate())
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Timed out at {phase}.");
                await Task.Delay(16);
            }
        }

        private static void AssertPng(byte[] data)
        {
            Assert.True(data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            using var bytes = GLib.Bytes.New(data);
            using var decoded = Gdk.Texture.NewFromBytes(bytes);
            Assert.True(decoded.GetWidth() > 0 && decoded.GetHeight() > 0);
        }
    }

    private sealed class ScreenshotAgent(AgentOptions options) : GtkAgentService(options)
    {
        public Task<byte[]?> CaptureNativeAsync(global::Gtk.Widget widget)
            => CaptureNativeElementScreenshotAsync(widget, null);
    }

    public sealed class ScreenshotApplication : Application
    {
        public Label FirstLabel { get; } = new() { Text = "First capture page", AutomationId = "capture-first" };
        public Label SecondLabel { get; } = new() { Text = "Second capture page", AutomationId = "capture-second" };
        public VerticalStackLayout FirstLayout { get; } = new();
        public VerticalStackLayout SecondLayout { get; } = new();
        public Shell Shell { get; } = new();
        public Label CurrentLabel => Shell.CurrentPage == FirstPage ? FirstLabel : SecondLabel;
        public VerticalStackLayout CurrentLayout => Shell.CurrentPage == FirstPage ? FirstLayout : SecondLayout;
        private ContentPage FirstPage { get; } = new();
        private ContentPage SecondPage { get; } = new();

        protected override Window CreateWindow(IActivationState? activationState)
        {
            FirstLayout.Children.Add(FirstLabel);
            SecondLayout.Children.Add(SecondLabel);
            FirstPage.Content = FirstLayout;
            SecondPage.Content = SecondLayout;
            Shell.Items.Add(new ShellContent { Route = "first", Content = FirstPage });
            Shell.Items.Add(new ShellContent { Route = "second", Content = SecondPage });
            return new Window(Shell) { Width = 600, Height = 400 };
        }
    }
}

internal sealed class GtkScreenshotRuntimeFactAttribute : FactAttribute
{
    public GtkScreenshotRuntimeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_GTK_RUNTIME_TESTS") != "1"
            && Environment.GetEnvironmentVariable("MAUI_GTK_RUNTIME_TESTS") != "1")
            Skip = "Requires GTK 4.12+, a display, and RUN_GTK_RUNTIME_TESTS=1.";
    }
}

[CollectionDefinition("GTK screenshot runtime", DisableParallelization = true)]
public sealed class GtkScreenshotRuntimeCollection;
