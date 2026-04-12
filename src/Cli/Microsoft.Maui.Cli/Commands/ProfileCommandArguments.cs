// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileCommandArguments
{
	internal static string[] BuildCompileArguments(
		string projectPath,
		string framework,
		string configuration,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-c", configuration,
			"-f", framework,
			"--nologo"
		};

		AppendDiagnosticArguments(args, transport, diagnosticPort);
		AppendBuildInjectionArguments(args, buildInjection);
		return [.. args];
	}

	internal static string[] BuildLaunchArguments(
		string projectPath,
		string framework,
		string configuration,
		Device device,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-t:Run",
			"-c", configuration,
			"-f", framework,
			"-p:WaitForExit=false",
		};

		AppendDiagnosticArguments(args, transport, diagnosticPort);

		if (string.Equals(transport.Platform, Platforms.Android, StringComparison.OrdinalIgnoreCase))
		{
			args.Add($"-p:AdbTarget=-s {device.Id}");
			args.Add("-p:AndroidEnableProfiler=true");
		}
		else if (string.Equals(transport.Platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase))
		{
			args.Add($"-p:_DeviceName=:v2:udid={device.Id}");
			args.Add("-p:_MlaunchWaitForExit=false");
		}

		AppendBuildInjectionArguments(args, buildInjection);
		return [.. args];
	}

	internal static string[] BuildDsrouterArguments(ProfileTransportConfiguration transport, int diagnosticPort)
	{
		var args = new List<string>
		{
			transport.DsrouterKind,
			transport.DsrouterRuntimeEndpointOption,
			$"{IPAddress.Loopback}:{diagnosticPort}"
		};

		if (!string.IsNullOrWhiteSpace(transport.DsrouterForwardPort))
		{
			args.Add("--forward-port");
			args.Add(transport.DsrouterForwardPort);
		}

		return [.. args];
	}

	static void AppendDiagnosticArguments(List<string> args, ProfileTransportConfiguration transport, int diagnosticPort)
	{
		args.Add($"-p:DiagnosticAddress={transport.DiagnosticAddress}");
		args.Add($"-p:DiagnosticPort={diagnosticPort}");
		args.Add("-p:DiagnosticSuspend=true");
		args.Add($"-p:DiagnosticListenMode={transport.DiagnosticListenMode}");
		args.Add("-p:EnableDiagnostics=true");
	}

	static void AppendBuildInjectionArguments(List<string> args, ProfilingBuildInjection? buildInjection)
	{
		if (buildInjection is null)
			return;

		args.Add($"-p:CustomAfterMicrosoftCommonTargets={buildInjection.TargetsPath}");
		args.Add("-p:MauiStartupProfilingInject=true");
		args.Add($"-p:MauiStartupProfilingExitHost={buildInjection.ExitControlHost}");
		args.Add($"-p:MauiStartupProfilingExitPort={buildInjection.ExitControlPort}");
		args.Add($"-p:MauiStartupProfilingInjectBootstrap={(buildInjection.InjectBootstrap ? "true" : "false")}");

		if (!string.IsNullOrWhiteSpace(buildInjection.AssemblyPath))
			args.Add($"-p:MauiStartupProfilingAssemblyPath={buildInjection.AssemblyPath}");
	}
}
