// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
		ProfilingBuildInjection? buildInjection,
		bool diagnosticSuspend = true)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-c", configuration,
			"-f", framework,
			"--nologo"
		};

		AppendEnableDiagnosticsArgument(args);

		if (!UsesRuntimeOwnedEventPipe(buildInjection))
			AppendDiagnosticArguments(args, transport, diagnosticPort, diagnosticSuspend);

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
		ProfilingBuildInjection? buildInjection,
		bool diagnosticSuspend = true)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-t:Run",
			"-c", configuration,
			"-f", framework,
			$"-p:Device={device.Id}",
			"-p:WaitForExit=false",
		};

		AppendEnableDiagnosticsArgument(args);

		if (!UsesRuntimeOwnedEventPipe(buildInjection))
			AppendDiagnosticArguments(args, transport, diagnosticPort, diagnosticSuspend);

		if (string.Equals(transport.Platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase))
		{
			args.Add("-p:_MlaunchWaitForExit=false");
		}

		AppendBuildInjectionArguments(args, buildInjection);
		return [.. args];
	}

	static void AppendDiagnosticArguments(List<string> args, ProfileTransportConfiguration transport, int diagnosticPort, bool diagnosticSuspend)
	{
		args.Add($"-p:DiagnosticAddress={transport.DiagnosticAddress}");
		args.Add($"-p:DiagnosticPort={diagnosticPort}");
		args.Add($"-p:DiagnosticSuspend={(diagnosticSuspend ? "true" : "false")}");
		args.Add($"-p:DiagnosticListenMode={transport.DiagnosticListenMode}");
	}

	static void AppendEnableDiagnosticsArgument(List<string> args)
		=> args.Add("-p:EnableDiagnostics=true");

	static void AppendBuildInjectionArguments(List<string> args, ProfilingBuildInjection? buildInjection)
	{
		if (buildInjection is null)
			return;

		args.Add($"-p:CustomAfterMicrosoftCommonTargets={buildInjection.TargetsPath}");
		args.Add("-p:MauiProfilingHelperInject=true");
		if (!string.IsNullOrWhiteSpace(buildInjection.ExitControlHost))
			args.Add($"-p:MauiProfilingHelperExitHost={buildInjection.ExitControlHost}");
		if (buildInjection.ExitControlPort > 0)
			args.Add($"-p:MauiProfilingHelperExitPort={buildInjection.ExitControlPort}");
		args.Add($"-p:MauiProfilingHelperInjectBootstrap={(buildInjection.InjectBootstrap ? "true" : "false")}");
		if (buildInjection.EnableRuntimePgo)
			args.Add("-p:MauiProfilingHelperEnableRuntimePgo=true");
		if (!string.IsNullOrWhiteSpace(buildInjection.EventPipeOutputPath))
			args.Add($"-p:MauiProfilingHelperEventPipeOutputPath={buildInjection.EventPipeOutputPath}");

		if (!string.IsNullOrWhiteSpace(buildInjection.AssemblyPath))
			args.Add($"-p:MauiProfilingHelperAssemblyPath={buildInjection.AssemblyPath}");
	}

	static bool UsesRuntimeOwnedEventPipe(ProfilingBuildInjection? buildInjection)
		=> !string.IsNullOrWhiteSpace(buildInjection?.EventPipeOutputPath);
}
