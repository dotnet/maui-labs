// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xamarin.MacDev;

namespace Microsoft.Maui.Cli.Providers.Apple;

/// <summary>
/// Shared parsing helpers that map CLI string tokens to the strongly-typed
/// enums exposed by <see cref="SimulatorService"/>. Centralised here so both the
/// <c>maui apple simulator</c> command tree and the DevFlow command layer accept
/// the same spellings without duplicating switch statements.
/// </summary>
public static class SimulatorEnumParsing
{
	static string Normalize(string value) =>
		value.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");

	/// <summary>
	/// Parses a privacy service token. Accepts the <c>simctl</c> spellings
	/// (e.g. <c>location-always</c>, <c>contacts-limited</c>, <c>photos-add</c>,
	/// <c>media-library</c>) as well as the enum names.
	/// </summary>
	public static bool TryParsePrivacyPermission(string value, out PrivacyPermission permission)
	{
		permission = PrivacyPermission.All;
		if (string.IsNullOrWhiteSpace(value))
			return false;

		switch (Normalize(value))
		{
			case "all": permission = PrivacyPermission.All; return true;
			case "calendar": permission = PrivacyPermission.Calendar; return true;
			case "contactslimited": permission = PrivacyPermission.ContactsLimited; return true;
			case "contacts": permission = PrivacyPermission.Contacts; return true;
			case "location": permission = PrivacyPermission.Location; return true;
			case "locationalways": permission = PrivacyPermission.LocationAlways; return true;
			case "photosadd": permission = PrivacyPermission.PhotosAdd; return true;
			case "photos": permission = PrivacyPermission.Photos; return true;
			case "medialibrary": permission = PrivacyPermission.MediaLibrary; return true;
			case "microphone": permission = PrivacyPermission.Microphone; return true;
			case "motion": permission = PrivacyPermission.Motion; return true;
			case "reminders": permission = PrivacyPermission.Reminders; return true;
			case "siri": permission = PrivacyPermission.Siri; return true;
			default: return false;
		}
	}

	/// <summary>
	/// Returns the comma-separated list of accepted privacy service tokens for error messages.
	/// </summary>
	public static string PrivacyPermissionNames =>
		"all, calendar, contacts, contacts-limited, location, location-always, photos, photos-add, media-library, microphone, motion, reminders, siri";

	/// <summary>Parses a screenshot format token (png, jpeg, tiff, bmp).</summary>
	public static bool TryParseScreenshotFormat(string value, out ScreenshotFormat format)
	{
		format = ScreenshotFormat.Png;
		switch (Normalize(value))
		{
			case "png": format = ScreenshotFormat.Png; return true;
			case "jpeg":
			case "jpg": format = ScreenshotFormat.Jpeg; return true;
			case "tiff": format = ScreenshotFormat.Tiff; return true;
			case "bmp": format = ScreenshotFormat.Bmp; return true;
			default: return false;
		}
	}

	/// <summary>Parses a video recording format token (mp4, h264, fmp4, gif).</summary>
	public static bool TryParseVideoFormat(string value, out VideoRecordingFormat format)
	{
		format = VideoRecordingFormat.Mp4;
		switch (Normalize(value))
		{
			case "mp4": format = VideoRecordingFormat.Mp4; return true;
			case "h264": format = VideoRecordingFormat.H264; return true;
			case "fmp4": format = VideoRecordingFormat.Fmp4; return true;
			case "gif": format = VideoRecordingFormat.Gif; return true;
			default: return false;
		}
	}

	/// <summary>Parses a battery state token (charging, charged, discharging).</summary>
	public static bool TryParseBatteryState(string value, out SimulatorBatteryState state)
	{
		state = SimulatorBatteryState.Charging;
		switch (Normalize(value))
		{
			case "charging": state = SimulatorBatteryState.Charging; return true;
			case "charged": state = SimulatorBatteryState.Charged; return true;
			case "discharging": state = SimulatorBatteryState.Discharging; return true;
			default: return false;
		}
	}

	/// <summary>Parses a data network token (wifi, 3g, 4g, lte, lte-a, lte+, 5g, 5g+, 5g-uc, 5g-a).</summary>
	public static bool TryParseDataNetwork(string value, out SimulatorDataNetwork network)
	{
		network = SimulatorDataNetwork.Wifi;
		// Note: Normalize() strips hyphens but preserves '+', so "lte-a" → "ltea" but
		// "lte+" stays "lte+". We need explicit cases for both the '+' spelling and
		// the expanded "lteplus"/"5gplus" forms.
		switch (Normalize(value))
		{
			case "wifi": network = SimulatorDataNetwork.Wifi; return true;
			case "3g": network = SimulatorDataNetwork.ThreeG; return true;
			case "4g": network = SimulatorDataNetwork.FourG; return true;
			case "lte": network = SimulatorDataNetwork.Lte; return true;
			case "ltea": network = SimulatorDataNetwork.LteA; return true;
			case "lte+":
			case "lteplus": network = SimulatorDataNetwork.LtePlus; return true;
			case "5g": network = SimulatorDataNetwork.FiveG; return true;
			case "5g+":
			case "5gplus": network = SimulatorDataNetwork.FiveGPlus; return true;
			case "5guc": network = SimulatorDataNetwork.FiveGUc; return true;
			case "5ga": network = SimulatorDataNetwork.FiveGA; return true;
			default: return false;
		}
	}
}
