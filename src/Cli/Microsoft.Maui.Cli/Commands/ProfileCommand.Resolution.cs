// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Services;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

public static partial class ProfileCommand
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

	internal static bool WasOptionExplicitlySpecified<T>(ParseResult parseResult, Option<T> option)
		=> parseResult.GetResult(option)?.Tokens.Count > 0;

	internal static string ResolveProfileConfiguration(string? requestedConfiguration, bool explicitlySpecified, string platform)
	{
		return string.IsNullOrWhiteSpace(requestedConfiguration)
			? "Release"
			: requestedConfiguration.Trim();
	}

	static Task<Device> ResolveProfileDeviceAsync(
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
			Platforms.Android => ResolveAndroidDeviceAsync(requestedDevice, deviceManager, nonInteractive, spectre, cancellationToken),
			Platforms.iOS => ResolveIosSimulatorAsync(requestedDevice, deviceManager, nonInteractive, spectre, cancellationToken),
			_ => Task.FromException<Device>(new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"Startup profiling is not implemented yet for platform '{platform}'.")),
		};
	}

	static async Task<Device> ResolveAndroidDeviceAsync(
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken)
	{
		var runningDevices = (await deviceManager.GetDevicesByPlatformAsync(Platforms.Android, cancellationToken))
			.Where(d => d.IsRunning)
			.ToList();

		if (!runningDevices.Any())
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.AndroidDeviceNotFound,
				"No running Android device or emulator was found.",
				[
					"Start an emulator with `maui android emulator start --name <name>`.",
					"Or connect a physical device over USB and verify it appears in `maui device list --platform android`."
				]);
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
					ErrorCodes.AndroidDeviceNotFound,
					$"Android device '{requestedDevice}' was not found among the running devices.");
			}

			return match;
		}

		if (runningDevices.Count == 1 || nonInteractive || spectre == null)
			return runningDevices[0];

		return spectre.Prompt(
			new SelectionPrompt<Device>()
				.Title("[bold]Select the Android device to profile[/]")
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(device =>
				{
					var type = device.IsEmulator ? "emulator" : "device";
					var version = string.IsNullOrWhiteSpace(device.VersionName) ? device.Version : device.VersionName;
					return $"[bold]{Markup.Escape(device.Name)}[/]  [grey]{Markup.Escape(device.Id)}[/]  [dim]{type} {Markup.Escape(version ?? string.Empty)}[/]";
				})
				.AddChoices(runningDevices));
	}

	static async Task<Device> ResolveIosSimulatorAsync(
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken)
	{
		var runningDevices = (await deviceManager.GetDevicesByPlatformAsync(Platforms.iOS, cancellationToken))
			.Where(d => d.IsRunning)
			.ToList();

		if (!runningDevices.Any())
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DeviceNotFound,
				"No booted iOS simulator was found.",
				[
					"Boot a simulator with `xcrun simctl boot <UDID>` or open Simulator.app and start one there.",
					"Then verify it appears in `maui device list --platform ios`."
				]);
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
					ErrorCodes.DeviceNotFound,
					$"iOS simulator '{requestedDevice}' was not found among the booted simulators.");
			}

			return match;
		}

		if (runningDevices.Count == 1 || nonInteractive || spectre == null)
			return runningDevices[0];

		return spectre.Prompt(
			new SelectionPrompt<Device>()
				.Title("[bold]Select the iOS simulator to profile[/]")
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(device =>
				{
					var version = string.IsNullOrWhiteSpace(device.VersionName) ? device.Version : device.VersionName;
					return $"[bold]{Markup.Escape(device.Name)}[/]  [grey]{Markup.Escape(device.Id)}[/]  [dim]simulator {Markup.Escape(version ?? string.Empty)}[/]";
				})
				.AddChoices(runningDevices));
	}

	internal static TraceOutputFormat ResolveTraceOutputFormat(
		string? requestedFormat,
		bool explicitlySpecified,
		bool nonInteractive,
		SpectreOutputFormatter? spectre)
	{
		if (explicitlySpecified || nonInteractive || spectre is null)
			return ResolveTraceOutputFormat(requestedFormat);

		return spectre.Prompt(
			new SelectionPrompt<TraceOutputFormat>()
				.Title("[bold]Select the trace output format[/]")
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(FormatTraceOutputPromptChoice)
				.AddChoices([TraceOutputFormat.NetTrace, TraceOutputFormat.Speedscope]));
	}

	internal static TraceOutputFormat ResolveTraceOutputFormat(string? requestedFormat) => requestedFormat?.Trim().ToLowerInvariant() switch
	{
		null or "" or "nettrace" => TraceOutputFormat.NetTrace,
		"speedscope" => TraceOutputFormat.Speedscope,
		_ => throw new MauiToolException(
			ErrorCodes.InvalidArgument,
			$"Unsupported output format '{requestedFormat}'. Supported values are: nettrace, speedscope.")
	};

	internal static string ResolveOutputPath(string projectName, string? requestedOutput, TraceOutputFormat outputFormat)
	{
		if (string.IsNullOrWhiteSpace(requestedOutput))
		{
			var safeProjectName = string.IsNullOrWhiteSpace(projectName) ? "maui-startup-profile" : projectName;
			var defaultName = $"{safeProjectName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.nettrace";
			return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, defaultName));
		}

		var fullPath = Path.GetFullPath(requestedOutput);
		if (outputFormat == TraceOutputFormat.Speedscope &&
			fullPath.EndsWith(SpeedscopeExtension, StringComparison.OrdinalIgnoreCase))
		{
			fullPath = fullPath[..^SpeedscopeExtension.Length];
		}

		if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
			fullPath += ".nettrace";
		return fullPath;
	}

	internal static string GetPrimaryOutputPath(string collectorOutputPath, TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.Speedscope => collectorOutputPath + SpeedscopeExtension,
		_ => collectorOutputPath
	};

	internal static string FormatOutputFormat(TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.NetTrace => "nettrace",
		TraceOutputFormat.Speedscope => "speedscope",
		_ => outputFormat.ToString().ToLowerInvariant()
	};

	static string FormatTraceOutputPromptChoice(TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.NetTrace => "[bold]nettrace[/] [dim](raw EventPipe trace for PerfView / Visual Studio)[/]",
		TraceOutputFormat.Speedscope => "[bold]speedscope[/] [dim](browser-friendly flame chart; also keeps the raw .nettrace)[/]",
		_ => $"[bold]{Markup.Escape(FormatOutputFormat(outputFormat))}[/]"
	};

	static void ValidateStoppingEventOptions(string? providerName, string? eventName, string? payloadFilter)
	{
		var hasProviderName = !string.IsNullOrWhiteSpace(providerName);
		var hasEventName = !string.IsNullOrWhiteSpace(eventName);
		var hasPayloadFilter = !string.IsNullOrWhiteSpace(payloadFilter);

		if (!hasProviderName && (hasEventName || hasPayloadFilter))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				"--stopping-event-provider-name is required when using --stopping-event-event-name or --stopping-event-payload-filter.");
		}

		if (!hasEventName && hasPayloadFilter)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				"--stopping-event-event-name is required when using --stopping-event-payload-filter.");
		}
	}

	internal static StoppingEventConfiguration ResolveStoppingEventConfiguration(
		TimeSpan? duration,
		string? providerName,
		string? eventName,
		string? payloadFilter)
	{
		return new StoppingEventConfiguration(providerName, eventName, payloadFilter, AutoSelected: false);
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

	internal static Version GetFrameworkSortKey(string tfm)
	{
		var match = Regex.Match(tfm, @"net(?<version>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
		if (!match.Success)
			return new Version(0, 0);

		return Version.TryParse(match.Groups["version"].Value, out var parsed)
			? parsed
			: new Version(0, 0);
	}
}
