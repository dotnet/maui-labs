// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Services;

/// <summary>
/// Service for managing devices across all platforms.
/// </summary>
public interface IDeviceManager
{
	Task<IReadOnlyList<Device>> GetAllDevicesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the devices for <paramref name="platform"/>, querying only the providers that
	/// can produce them. Accepts any value handled by <see cref="Platforms.Normalize"/>,
	/// including <see cref="Platforms.All"/>.
	/// </summary>
	/// <remarks>
	/// Implementations must not enumerate every provider and then filter: provider queries are
	/// expensive (the Apple provider shells out to <c>simctl</c>) and asking for one platform
	/// must never pay another platform's cost.
	/// <para>
	/// Valid platforms may have no backing provider (Mac Catalyst and Windows do not today), in
	/// which case this returns an empty list rather than failing. Callers that want to report
	/// "not supported yet" separately from "none found" can check
	/// <see cref="DeviceManager.HasProviderFor"/>.
	/// </para>
	/// </remarks>
	Task<IReadOnlyList<Device>> GetDevicesByPlatformAsync(string platform, CancellationToken cancellationToken = default);
	Task<Device?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default);
	Task<Device> GetRunningDeviceOrThrowAsync(CancellationToken cancellationToken = default);
}
