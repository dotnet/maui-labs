using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class DeviceInspectorControlTests
{
    private const string DeviceId = "android:emulator:pixel";

    private sealed class RecordingDeviceSurface : IDeviceSurface
    {
        public List<string> Calls { get; } = [];
        public string CreatedDeviceState { get; set; } = DeviceStates.Booted;
        public TaskCompletionSource<bool>? RestartStarted { get; set; }
        public TaskCompletionSource<bool>? ReleaseRestart { get; set; }

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });

        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([Target()]);

        public Task<DeviceCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceCatalog?>(new DeviceCatalog
            {
                Devices = [Target()],
                Runtimes =
                [
                    new DeviceRuntime
                    {
                        Id = "android-35",
                        Name = "Android 15",
                        Version = "35",
                        Platform = DevicePlatforms.Android,
                        IsAvailable = true,
                        SupportedDeviceTypeIds = ["pixel_8"],
                    },
                ],
                DeviceTypes =
                [
                    new Microsoft.Maui.DevFlow.Devices.DeviceType
                    {
                        Id = "pixel_8",
                        Name = "Pixel 8",
                        Platform = DevicePlatforms.Android,
                    },
                ],
                Diagnostics =
                [
                    new DeviceHostDiagnostics
                    {
                        Platform = DevicePlatforms.Android,
                        Ready = true,
                        Checks =
                        [
                            new DeviceDependencyCheck
                            {
                                Name = "adb",
                                Status = "ready",
                                Message = "Android tooling is available.",
                                Path = @"C:\Users\person\secret\adb.exe",
                            },
                        ],
                    },
                ],
            });

        public Task<DeviceTarget?> GetAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceTarget?>(Target());

        public Task<DeviceOperationResult> CreateAsync(
            DeviceCreateRequest request,
            CancellationToken cancellationToken = default) =>
            Record("create", Target() with
            {
                Id = "android:emulator:created",
                Name = request.Name,
                State = CreatedDeviceState,
            });

        public Task<DeviceOperationResult> BootAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Record("boot");

        public Task<DeviceOperationResult> ShutdownAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Record("shutdown");

        public async Task<DeviceOperationResult> RestartAsync(
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("restart");
            RestartStarted?.TrySetResult(true);
            if (ReleaseRestart is not null)
                await ReleaseRestart.Task.WaitAsync(cancellationToken);
            return DeviceOperationResult.Ok();
        }

        public Task<DeviceOperationResult> RevealAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Record("reveal");

        public Task<DeviceOperationResult> EraseAsync(
            string deviceId,
            bool confirm,
            CancellationToken cancellationToken = default) =>
            Record(confirm ? "erase" : "erase-unconfirmed");

        public Task<DeviceOperationResult> DeleteAsync(
            string deviceId,
            bool confirm,
            CancellationToken cancellationToken = default) =>
            Record(confirm ? "delete" : "delete-unconfirmed");

        public Task<DeviceOperationResult> TapAsync(
            string deviceId,
            DevicePoint point,
            CancellationToken cancellationToken = default) =>
            Record($"tap:{point.X}:{point.Y}");

        public Task<DeviceOperationResult> LongPressAsync(
            string deviceId,
            DevicePoint point,
            double duration = 1,
            CancellationToken cancellationToken = default) =>
            Record($"long-press:{duration}");

        public Task<DeviceOperationResult> SwipeAsync(
            string deviceId,
            DeviceSwipe swipe,
            CancellationToken cancellationToken = default) =>
            Record($"swipe:{swipe.StartX}:{swipe.EndX}");

        public Task<DeviceOperationResult> TypeTextAsync(
            string deviceId,
            string text,
            CancellationToken cancellationToken = default) =>
            Record($"type:{text}");

        public Task<DeviceOperationResult> PressKeyAsync(
            string deviceId,
            ulong keyCode,
            CancellationToken cancellationToken = default) =>
            Record($"key:{keyCode}");

        public Task<DeviceOperationResult> PressButtonAsync(
            string deviceId,
            string button,
            CancellationToken cancellationToken = default) =>
            Record($"button:{button}");

        public Task<DeviceOperationResult> RotateAsync(
            string deviceId,
            string orientation,
            CancellationToken cancellationToken = default) =>
            Record($"rotate:{orientation}");

        public Task<DeviceOperationResult> StartRecordingAsync(
            string deviceId,
            int timeoutSeconds = 180,
            CancellationToken cancellationToken = default) =>
            Record($"recording-start:{timeoutSeconds}");

        public Task<DeviceRecordingResult> StopRecordingAsync(
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("recording-stop");
            return Task.FromResult(new DeviceRecordingResult(true, "recording.mp4"));
        }

        public Task<DeviceRecordingStatus?> GetRecordingStatusAsync(
            string deviceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceRecordingStatus?>(new DeviceRecordingStatus
            {
                DeviceId = deviceId,
                IsRecording = Calls.LastOrDefault()?.StartsWith("recording-start", StringComparison.Ordinal) == true,
                OutputPath = Calls.LastOrDefault() == "recording-stop" ? "recording.mp4" : null,
            });

        public Task<byte[]?> ScreenshotAsync(string deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>([0x89, 0x50, 0x4e, 0x47]);

        private Task<DeviceOperationResult> Record(string call, DeviceTarget? target = null)
        {
            Calls.Add(call);
            return Task.FromResult(DeviceOperationResult.Ok(target));
        }

        private static DeviceTarget Target() => new()
        {
            Id = DeviceId,
            Platform = DevicePlatforms.Android,
            Provider = "android-emulator",
            NativeId = "emulator-5554",
            Name = "Pixel 8",
            State = DeviceStates.Booted,
            IsAvailable = true,
            DeviceSet = @"C:\Users\person\secret\devices",
            RuntimeId = "android-35",
            DeviceTypeId = "pixel_8",
            Display = new DisplayGeometry { PointWidth = 412, PointHeight = 915, Scale = 2.625 },
            Capabilities = new DeviceCapabilities
            {
                Boot = true,
                Shutdown = true,
                Restart = true,
                Erase = true,
                Delete = true,
                Reveal = true,
                Tap = true,
                LongPress = true,
                Swipe = true,
                Text = true,
                Key = true,
                Button = true,
                Rotate = true,
                Screenshot = true,
                Recording = true,
            },
        };
    }

    [Fact]
    public async Task CatalogAndScreenshotExposeTheIntegratedDeviceSurface()
    {
        var surface = new RecordingDeviceSurface();
        var port = FreePort();
        using var broker = new BrokerServer(port, new DeviceRegistry(surface));
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(port);

        try
        {
            using var http = new HttpClient();
            using var catalog = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/api/devices/catalog"));
            Assert.True(catalog.RootElement.GetProperty("available").GetBoolean());
            Assert.Equal(DeviceId, catalog.RootElement.GetProperty("devices")[0].GetProperty("id").GetString());
            Assert.True(catalog.RootElement.GetProperty("devices")[0].GetProperty("capabilities").GetProperty("rotate").GetBoolean());
            Assert.Equal("android-35", catalog.RootElement.GetProperty("runtimes")[0].GetProperty("id").GetString());
            Assert.DoesNotContain(@"C:\Users\person\secret", catalog.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.False(catalog.RootElement.GetProperty("devices")[0].TryGetProperty("deviceSet", out _));

            using var forbiddenScreenshot = await http.GetAsync(
                $"http://127.0.0.1:{port}/api/devices/{Uri.EscapeDataString(DeviceId)}/screenshot");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenScreenshot.StatusCode);

            var embedToken = (string)typeof(BrokerServer)
                .GetField("_embedToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(broker)!;
            using var screenshotRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{port}/api/devices/{Uri.EscapeDataString(DeviceId)}/screenshot");
            screenshotRequest.Headers.TryAddWithoutValidation("X-DevFlow-Embed-Token", embedToken);
            var screenshot = await http.SendAsync(screenshotRequest);
            Assert.Equal(HttpStatusCode.OK, screenshot.StatusCode);
            Assert.Equal("image/png", screenshot.Content.Headers.ContentType?.MediaType);
            Assert.Equal([0x89, 0x50, 0x4e, 0x47], await screenshot.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task DeviceControlsUseLeaseAndRequireExactDestructiveConfirmation()
    {
        var surface = new RecordingDeviceSurface();
        var port = FreePort();
        using var broker = new BrokerServer(port, new DeviceRegistry(surface));
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(port);

        try
        {
            using var http = new HttpClient();
            var endpoint = $"http://127.0.0.1:{port}/api/devices/control" +
                "?leaseId=inspector-device-test&holderKind=web-inspector&label=Inspector";

            var typed = await http.PostAsJsonAsync(endpoint, new
            {
                action = "type-text",
                deviceId = DeviceId,
                text = "hello",
            });
            Assert.Equal(HttpStatusCode.OK, typed.StatusCode);
            Assert.Contains("type:hello", surface.Calls);

            var otherEndpoint = endpoint.Replace("leaseId=inspector-device-test", "leaseId=another-inspector", StringComparison.Ordinal);
            var secondWriter = await http.PostAsJsonAsync(otherEndpoint, new
            {
                action = "press-button",
                deviceId = DeviceId,
                button = "home",
            });
            Assert.Equal(HttpStatusCode.OK, secondWriter.StatusCode);
            Assert.Contains("button:home", surface.Calls);

            var unconfirmed = await http.PostAsJsonAsync(endpoint, new
            {
                action = "delete",
                deviceId = DeviceId,
                confirm = true,
                confirmedDeviceId = DeviceId,
                confirmationText = "delete something-else",
            });
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
            Assert.DoesNotContain("delete", surface.Calls);

            var confirmed = await http.PostAsJsonAsync(endpoint, new
            {
                action = "delete",
                deviceId = DeviceId,
                confirm = true,
                confirmedDeviceId = DeviceId,
                confirmationText = $"delete {DeviceId}",
            });
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            Assert.Contains("delete", surface.Calls);
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CreateBootsADeviceWhenTheHostReturnsItShutdown()
    {
        var surface = new RecordingDeviceSurface { CreatedDeviceState = DeviceStates.Shutdown };
        var port = FreePort();
        using var broker = new BrokerServer(port, new DeviceRegistry(surface));
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(port);

        try
        {
            using var http = new HttpClient();
            var endpoint = $"http://127.0.0.1:{port}/api/devices/control" +
                "?leaseId=create-test&holderKind=web-inspector&label=Inspector";
            var response = await http.PostAsJsonAsync(endpoint, new
            {
                action = "create",
                platform = "android",
                name = "Created Pixel",
                runtimeId = "android-35",
                deviceTypeId = "pixel_8",
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(["create", "boot"], surface.Calls);
            using var responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "android:emulator:created",
                responseBody.RootElement.GetProperty("device").GetProperty("id").GetString());
            Assert.True(responseBody.RootElement.GetProperty("device").GetProperty("isBooted").GetBoolean());
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SameInspectorWriterCannotOverlapDeviceOperations()
    {
        var surface = new RecordingDeviceSurface
        {
            RestartStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseRestart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var port = FreePort();
        using var broker = new BrokerServer(port, new DeviceRegistry(surface));
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(port);

        try
        {
            using var http = new HttpClient();
            var endpoint = $"http://127.0.0.1:{port}/api/devices/control" +
                "?leaseId=one-inspector-writer&holderKind=web-inspector&label=Inspector";

            var restart = http.PostAsJsonAsync(endpoint, new
            {
                action = "restart",
                deviceId = DeviceId,
            });
            await surface.RestartStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var tap = await http.PostAsJsonAsync(endpoint, new
            {
                action = "tap",
                deviceId = DeviceId,
                x = 10,
                y = 20,
            });

            Assert.Equal(HttpStatusCode.Conflict, tap.StatusCode);
            Assert.DoesNotContain(surface.Calls, call => call.StartsWith("tap:", StringComparison.Ordinal));

            surface.ReleaseRestart.SetResult(true);
            Assert.Equal(HttpStatusCode.OK, (await restart).StatusCode);
        }
        finally
        {
            surface.ReleaseRestart?.TrySetResult(true);
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task LegacyCliControlUsesAnOperationScopedLease()
    {
        var surface = new RecordingDeviceSurface();
        var port = FreePort();
        using var broker = new BrokerServer(port, new DeviceRegistry(surface));
        using var cancellation = new CancellationTokenSource();
        var brokerTask = broker.RunAsync(cancellation.Token);
        await WaitForBrokerAsync(port);

        try
        {
            var boot = await BrokerClient.ControlDeviceAsync(port, DeviceId, "boot");
            var shutdown = await BrokerClient.ControlDeviceAsync(port, DeviceId, "shutdown");

            Assert.True(boot.Success, boot.Reason);
            Assert.True(shutdown.Success, shutdown.Reason);
            Assert.Contains("boot", surface.Calls);
            Assert.Contains("shutdown", surface.Calls);
        }
        finally
        {
            cancellation.Cancel();
            broker.Dispose();
            await brokerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static int FreePort() => TestPorts.Reserve();

    private static async Task WaitForBrokerAsync(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(20);
        }

        throw new TimeoutException("Broker did not start.");
    }
}
