// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Services;

internal static class CliUpdatePolicy
{
	internal static bool ShouldCheck(string[] args, bool json, bool ci, bool dryRun,
		bool redirected, Func<string, string?> environment, bool globalInstall)
	{
		if (!globalInstall || dryRun ||
			environment("MAUI_CLI_NO_UPDATE_CHECK") == "1")
			return false;

		if (!IsInteractive(json, ci, redirected, environment))
			return false;

		// Protocol, server, and streaming commands never participate in notifications.
		var command = args.FirstOrDefault(arg => !arg.StartsWith('-') && arg is not ("true" or "false"));
		return command is "doctor" or "device" or "android" or "apple" or "project" or "version" or "port" or "profile";
	}

	internal static bool IsInteractive(bool json, bool ci, bool redirected, Func<string, string?> environment)
	{
		if (json || ci || redirected)
			return false;

		foreach (var key in new[] { "CI", "TF_BUILD", "GITHUB_ACTIONS", "BUILD_BUILDID", "JENKINS_URL", "TEAMCITY_VERSION" })
		{
			var value = environment(key);
			if (!string.IsNullOrEmpty(value) && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
				return false;
		}

		return true;
	}
}
