// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Services;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileTargetResolver
{
	internal static string ResolveTargetFramework(
		ResolvedMauiProject project,
		string? requestedFramework,
		string platform,
		bool nonInteractive,
		SpectreOutputFormatter? spectre)
	{
		if (!string.IsNullOrWhiteSpace(requestedFramework))
		{
			var match = project.TargetFrameworks.FirstOrDefault(tfm =>
				string.Equals(tfm, requestedFramework, StringComparison.OrdinalIgnoreCase));

			if (match == null)
			{
				throw new MauiToolException(
					ErrorCodes.InvalidArgument,
					$"Target framework '{requestedFramework}' was not found in {Path.GetFileName(project.ProjectPath)}.");
			}

			if (!IsTargetFrameworkCompatible(match, platform))
			{
				throw new MauiToolException(
					ErrorCodes.PlatformNotSupported,
					$"Target framework '{requestedFramework}' does not target platform '{platform}'.");
			}

			return match;
		}

		var candidates = project.TargetFrameworks
			.Where(tfm => IsTargetFrameworkCompatible(tfm, platform))
			.OrderByDescending(GetFrameworkSortKey)
			.ThenBy(GetFrameworkPlatformPriority)
			.ToList();

		if (candidates.Count == 0)
		{
			var platformDescription = string.Equals(platform, Platforms.All, StringComparison.OrdinalIgnoreCase)
				? "No target frameworks were found"
				: $"No target framework in {Path.GetFileName(project.ProjectPath)} matches platform '{platform}'";
			throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				platformDescription + ".");
		}

		if (candidates.Count == 1 || nonInteractive || spectre == null)
			return candidates[0];

		var title = string.Equals(platform, Platforms.All, StringComparison.OrdinalIgnoreCase)
			? "[bold]Select the target framework to profile[/]"
			: $"[bold]Select the {Markup.Escape(platform)} target framework to profile[/]";

		return spectre.Prompt(
			new SelectionPrompt<string>()
				.Title(title)
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(FormatFrameworkPromptChoice)
				.AddChoices(candidates));
	}

	internal static Task<Device> ResolveProfileDeviceAsync(
		string platform,
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken)
	{
		var normalizedPlatform = Platforms.Normalize(platform);
		return normalizedPlatform switch
		{
			Platforms.Android => ResolveRunningDeviceAsync(
				platform: Platforms.Android,
				requestedDevice,
				deviceManager,
				nonInteractive,
				spectre,
				cancellationToken,
				notFoundCode: ErrorCodes.AndroidDeviceNotFound,
				notFoundMessage: "No running Android device or emulator was found.",
				notFoundSuggestions:
				[
					"Start an emulator with `maui android emulator start --name <name>`.",
					"Or connect a physical device over USB and verify it appears in `maui device list --platform android`."
				],
				missingRequestedMessage: value => $"Android device '{value}' was not found among the running devices.",
				selectionTitle: "[bold]Select the Android device to profile[/]",
				descriptionFactory: device =>
				{
					var type = device.IsEmulator ? "emulator" : "device";
					var version = string.IsNullOrWhiteSpace(device.VersionName) ? device.Version : device.VersionName;
					return $"{type} {version ?? string.Empty}".TrimEnd();
				}),
			Platforms.iOS => ResolveRunningDeviceAsync(
				platform: Platforms.iOS,
				requestedDevice,
				deviceManager,
				nonInteractive,
				spectre,
				cancellationToken,
				notFoundCode: ErrorCodes.DeviceNotFound,
				notFoundMessage: "No booted iOS simulator was found.",
				notFoundSuggestions:
				[
					"Boot a simulator with `xcrun simctl boot <UDID>` or open Simulator.app and start one there.",
					"Then verify it appears in `maui device list --platform ios`."
				],
				missingRequestedMessage: value => $"iOS simulator '{value}' was not found among the booted simulators.",
				selectionTitle: "[bold]Select the iOS simulator to profile[/]",
				descriptionFactory: device =>
				{
					var version = string.IsNullOrWhiteSpace(device.VersionName) ? device.Version : device.VersionName;
					return $"simulator {version ?? string.Empty}".TrimEnd();
				}),
			_ => Task.FromException<Device>(new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"Startup profiling is not implemented yet for platform '{platform}'.")),
		};
	}

	internal static bool IsTargetFrameworkCompatible(string tfm, string platform) => Platforms.Normalize(platform) switch
	{
		Platforms.All => true,
		Platforms.Android => tfm.Contains("-android", StringComparison.OrdinalIgnoreCase),
		Platforms.iOS => tfm.Contains("-ios", StringComparison.OrdinalIgnoreCase),
		Platforms.MacCatalyst => tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase),
		Platforms.Windows => tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase),
		_ => false
	};

	internal static string ResolveProfilePlatform(string requestedPlatform, string framework)
	{
		var normalizedPlatform = Platforms.Normalize(requestedPlatform);
		if (!string.Equals(normalizedPlatform, Platforms.All, StringComparison.OrdinalIgnoreCase))
			return normalizedPlatform;

		return InferPlatformFromTargetFramework(framework) ?? Platforms.All;
	}

	internal static string? InferPlatformFromTargetFramework(string tfm)
	{
		if (string.IsNullOrWhiteSpace(tfm))
			return null;

		if (tfm.Contains("-android", StringComparison.OrdinalIgnoreCase))
			return Platforms.Android;
		if (tfm.Contains("-ios", StringComparison.OrdinalIgnoreCase))
			return Platforms.iOS;
		if (tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase))
			return Platforms.MacCatalyst;
		if (tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase))
			return Platforms.Windows;

		return null;
	}

	internal static Version GetFrameworkSortKey(string tfm)
	{
		var match = Regex.Match(tfm, @"net(?<version>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
		if (!match.Success)
			return new Version(0, 0);

		return Version.TryParse(match.Groups["version"].Value, out var parsed)
			? parsed
			: new Version(0, 0);
	}

	static async Task<Device> ResolveRunningDeviceAsync(
		string platform,
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken,
		string notFoundCode,
		string notFoundMessage,
		IReadOnlyList<string> notFoundSuggestions,
		Func<string, string> missingRequestedMessage,
		string selectionTitle,
		Func<Device, string> descriptionFactory)
	{
		var runningDevices = (await deviceManager.GetDevicesByPlatformAsync(platform, cancellationToken))
			.Where(d => d.IsRunning)
			.ToList();

		if (!runningDevices.Any())
		{
			throw MauiToolException.UserActionRequired(
				notFoundCode,
				notFoundMessage,
				[.. notFoundSuggestions]);
		}

		if (!string.IsNullOrWhiteSpace(requestedDevice))
		{
			var match = runningDevices.FirstOrDefault(device =>
				string.Equals(device.Id, requestedDevice, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(device.EmulatorId, requestedDevice, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(device.Name, requestedDevice, StringComparison.OrdinalIgnoreCase));

			if (match == null)
			{
				throw new MauiToolException(
					notFoundCode,
					missingRequestedMessage(requestedDevice));
			}

			return match;
		}

		if (runningDevices.Count == 1 || nonInteractive || spectre == null)
			return runningDevices[0];

		return spectre.Prompt(
			new SelectionPrompt<Device>()
				.Title(selectionTitle)
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(FormatDevicePromptChoice(descriptionFactory))
				.AddChoices(runningDevices));
	}

	static int GetFrameworkPlatformPriority(string tfm) => InferPlatformFromTargetFramework(tfm) switch
	{
		Platforms.Android => 0,
		Platforms.iOS => 1,
		Platforms.MacCatalyst => 2,
		Platforms.Windows => 3,
		_ => 4
	};

	static string FormatFrameworkPromptChoice(string tfm)
	{
		var platform = InferPlatformFromTargetFramework(tfm);
		return string.IsNullOrWhiteSpace(platform)
			? $"[bold]{Markup.Escape(tfm)}[/]"
			: $"[bold]{Markup.Escape(tfm)}[/] [dim]({Markup.Escape(platform)})[/]";
	}

	static Func<Device, string> FormatDevicePromptChoice(Func<Device, string> descriptionFactory) => device =>
	{
		var description = descriptionFactory(device);
		return string.IsNullOrWhiteSpace(description)
			? $"[bold]{Markup.Escape(device.Name)}[/]  [grey]{Markup.Escape(device.Id)}[/]"
			: $"[bold]{Markup.Escape(device.Name)}[/]  [grey]{Markup.Escape(device.Id)}[/]  [dim]{Markup.Escape(description)}[/]";
	};
}
