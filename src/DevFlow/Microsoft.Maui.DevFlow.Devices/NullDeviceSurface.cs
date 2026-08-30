namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// The device surface used when there is no device layer: no host installed, or a desktop MAUI
/// app that has no virtual device around it.
/// <para>
/// It reports everything as unavailable and refuses every operation with a reason. This is what
/// keeps the "device layer absent means today's behaviour, unchanged" guarantee honest — callers
/// take the same code path either way and simply find every capability false.
/// </para>
/// </summary>
public sealed class NullDeviceSurface : IDeviceSurface
{
    public static readonly NullDeviceSurface Instance = new();

    private readonly DeviceHostHealth _health;

    public NullDeviceSurface(string? reason = null) =>
        _health = reason is null
            ? DeviceHostHealth.Unavailable
            : new DeviceHostHealth { Availability = DeviceHostAvailability.Absent, Reason = reason };

    public Task<DeviceHostHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_health);

    public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken cancellationToken = default) =>
        // Enumeration succeeded and found nothing, which is the truth when there is no device
        // layer at all — not a failure to enumerate.
        Task.FromResult<IReadOnlyList<DeviceTarget>?>([]);

    public Task<DeviceTarget?> GetAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<DeviceTarget?>(null);

    public Task<DeviceOperationResult> BootAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeviceOperationResult.NoHost());

    public Task<DeviceOperationResult> ShutdownAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeviceOperationResult.NoHost());

    public Task<DeviceOperationResult> TapAsync(string deviceId, DevicePoint point, CancellationToken cancellationToken = default) =>
        Task.FromResult(DeviceOperationResult.NoHost());

    public Task<byte[]?> ScreenshotAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);
}
