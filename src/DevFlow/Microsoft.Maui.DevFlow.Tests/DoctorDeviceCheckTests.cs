using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The device layer's entry in <c>maui doctor</c>.
/// <para>
/// The distinction these pin is between <em>absent</em> and <em>broken</em>. Absence is normal on
/// most machines and must not read as a problem; a host that exists but cannot be driven must,
/// because otherwise a fixable integration failure looks exactly like a feature nobody installed.
/// </para>
/// </summary>
public class DoctorDeviceCheckTests
{
    private sealed class FixedHealthSurface(DeviceHostHealth health) : IDeviceSurface
    {
        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken ct = default) => Task.FromResult(health);
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([]);
        public Task<DeviceTarget?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<DeviceTarget?>(null);
        public Task<DeviceOperationResult> BootAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.NoHost());
        public Task<DeviceOperationResult> ShutdownAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.NoHost());
        public Task<DeviceOperationResult> TapAsync(string id, DevicePoint p, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.NoHost());
        public Task<byte[]?> ScreenshotAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }

    private static Task<HealthCheck> Check(DeviceHostHealth health) =>
        DoctorService.CheckDeviceLayerAsync(new FixedHealthSurface(health));

    [Fact]
    public async Task AbsentHost_IsSkipped_NotAFailure()
    {
        // Most machines have no device host. Reporting that as an error would train people to
        // ignore doctor output.
        var check = await Check(DeviceHostHealth.Unavailable);

        Assert.Equal(CheckStatus.Skipped, check.Status);
        Assert.Null(check.Fix);
    }

    [Fact]
    public async Task AvailableHost_IsOk()
    {
        var check = await Check(new DeviceHostHealth
        {
            Availability = DeviceHostAvailability.Available,
            Version = "0.1.6",
        });

        Assert.Equal(CheckStatus.Ok, check.Status);
        Assert.Contains("0.1.6", check.Message);
    }

    [Fact]
    public async Task UnauthorizedHost_WarnsWithAFix()
    {
        // The case that must never be silent: a host is there, and we cannot drive it.
        var check = await Check(DeviceHostHealth.Unauthorized());

        Assert.Equal(CheckStatus.Warning, check.Status);
        Assert.NotNull(check.Fix);
        Assert.Contains("token", check.Fix!.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncompatibleHost_WarnsAndSaysToAlignVersions()
    {
        var check = await Check(DeviceHostHealth.Incompatible("2.0"));

        Assert.Equal(CheckStatus.Warning, check.Status);
        Assert.NotNull(check.Fix);
        Assert.Contains("version", check.Fix!.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotRespondingHost_Warns()
    {
        var check = await Check(DeviceHostHealth.NotResponding("The device host is not responding."));

        Assert.Equal(CheckStatus.Warning, check.Status);
    }

    [Fact]
    public async Task Check_IsCategorisedUnderDevFlow()
    {
        var check = await Check(DeviceHostHealth.Unavailable);

        Assert.Equal("devflow", check.Category);
        Assert.False(string.IsNullOrWhiteSpace(check.Message));
    }
}
