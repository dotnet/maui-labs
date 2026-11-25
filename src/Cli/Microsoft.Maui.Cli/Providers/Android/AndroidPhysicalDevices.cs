// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// Groups <c>adb devices</c> entries by physical device. A phone connected over both USB and
/// wireless debugging is listed twice (e.g. <c>3A021FDH2000LA</c> and
/// <c>adb-3A021FDH2000LA-AbCdEf._adb-tls-connect._tcp</c> or <c>192.168.1.20:41234</c>), but
/// callers that need "the one connected device" should treat those transports as one device.
/// </summary>
internal static partial class AndroidPhysicalDevices
{
	[GeneratedRegex(@"^adb-(?<serial>.+)-[^-.]+\._adb-tls-connect\._tcp\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex MdnsTransportRegex();

	/// <summary>
	/// Returns the device serial embedded in an mDNS wireless-debugging transport id, or
	/// <c>null</c> when <paramref name="transportId"/> is not such an id.
	/// </summary>
	public static string? TryParseMdnsSerial(string transportId)
	{
		var match = MdnsTransportRegex().Match(transportId);
		return match.Success ? match.Groups["serial"].Value : null;
	}

	/// <summary>
	/// True when the transport id is a network (wireless debugging / <c>adb connect</c>) transport.
	/// </summary>
	public static bool IsNetworkTransport(string transportId)
		=> transportId.Contains("._adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase)
			|| transportId.Contains("._adb._tcp", StringComparison.OrdinalIgnoreCase)
			|| (transportId.Contains(':') && !transportId.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Identity of the physical device behind a transport: the device-reported serial number when
	/// known, otherwise the serial parsed from an mDNS transport id, otherwise the transport id.
	/// Emulators always use their transport id.
	/// </summary>
	public static string GetPhysicalId(string transportId, string? serialNumber, bool isEmulator)
	{
		if (isEmulator)
			return transportId;
		if (!string.IsNullOrWhiteSpace(serialNumber))
			return serialNumber.Trim();
		return TryParseMdnsSerial(transportId) ?? transportId;
	}

	public static string GetPhysicalId(Device device)
		=> GetPhysicalId(device.Id, device.SerialNumber, device.IsEmulator);

	/// <summary>
	/// Groups transports by physical device, preserving first-seen order. Within each group the
	/// preferred transport comes first: USB before network transports.
	/// </summary>
	public static IReadOnlyList<IReadOnlyList<T>> Group<T>(IEnumerable<T> transports, Func<T, string> transportId, Func<T, string> physicalId)
		=> transports
			.GroupBy(physicalId, StringComparer.OrdinalIgnoreCase)
			.Select(g => (IReadOnlyList<T>)g
				.OrderBy(t => IsNetworkTransport(transportId(t)) ? 1 : 0)
				.ToArray())
			.ToArray();

	public static IReadOnlyList<IReadOnlyList<Device>> Group(IEnumerable<Device> devices)
		=> Group(devices, static d => d.Id, GetPhysicalId);

	/// <summary>
	/// Finds the transport for a user-supplied serial (<c>--device</c> / <c>ANDROID_SERIAL</c>).
	/// An exact transport id wins; otherwise a physical serial matches the preferred transport of
	/// that device.
	/// </summary>
	public static T? FindRequested<T>(IEnumerable<T> transports, string requested, Func<T, string> transportId, Func<T, string> physicalId)
		where T : class
	{
		var list = transports as IReadOnlyCollection<T> ?? transports.ToArray();
		var exact = list.FirstOrDefault(t => transportId(t).Equals(requested, StringComparison.OrdinalIgnoreCase));
		if (exact is not null)
			return exact;

		return Group(list.Where(t => physicalId(t).Equals(requested, StringComparison.OrdinalIgnoreCase)), transportId, physicalId)
			.FirstOrDefault()?[0];
	}
}
