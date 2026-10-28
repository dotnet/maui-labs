// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

/// <summary>
/// Builds <c>xcrun devicectl list devices --json-output</c> documents (JSON version 5) for tests.
/// A null argument leaves the property out, as devicectl does for values it does not know.
/// </summary>
internal static class DeviceCtlFixtures
{
	public const string IPhoneUdid = "00008130-001A2B3C4D5E6F78";
	public const string IPadUdid = "00008112-000E1F2A3B4C5D6E";

	static readonly JsonSerializerOptions s_options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	public static string Document(params object[] devices) => JsonSerializer.Serialize(new
	{
		info = new { jsonVersion = 5, outcome = "success" },
		result = new { devices }
	}, s_options);

	/// <summary>A connected, paired iPhone; override the arguments for other kinds of device or states.</summary>
	/// <param name="identifier">The CoreDevice identifier, which changes between connections to the same device.</param>
	public static object IPhone(
		string identifier,
		string? udid = IPhoneUdid,
		string name = "Test iPhone",
		string deviceType = "iPhone",
		string productType = "iPhone17,1",
		string? marketingName = "iPhone 16 Pro",
		string platform = "iOS",
		string transportType = "wired",
		string pairingState = "paired",
		string connectionState = "connected") => new
		{
			identifier,
			properties = new
			{
				connection = new { pairingState, state = connectionState, transportType },
				hardware = new { deviceType, platform, productType, marketingName, reality = "physical", udid },
				software = new { osVersionNumber = new { stringValue = "18.1" } },
				state = new { name }
			}
		};

	public static object IPad(string identifier, string? udid = IPadUdid) => IPhone(
		identifier,
		udid,
		name: "Test iPad",
		deviceType: "iPad",
		productType: "iPad14,3",
		marketingName: "iPad Pro 11-inch (4th generation)");
}
