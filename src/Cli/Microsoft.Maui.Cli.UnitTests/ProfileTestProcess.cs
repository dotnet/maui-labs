// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length == 0)
	return 2;

if (args[0] == "wrap-ignore-stdin" && args.Length == 2)
{
	var dotnetHost = Path.GetFullPath(Path.Combine(
		RuntimeEnvironment.GetRuntimeDirectory(),
		"..",
		"..",
		"..",
		OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
	var startInfo = new ProcessStartInfo(dotnetHost)
	{
		UseShellExecute = false,
		RedirectStandardInput = true,
		RedirectStandardOutput = true,
		RedirectStandardError = true,
		CreateNoWindow = true
	};
	foreach (var argument in new[] { "run", "--file", args[1], "--no-launch-profile", "--", "ignore-stdin" })
		startInfo.ArgumentList.Add(argument);

	using var child = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the child test process.");
	var outputPump = PumpAsync(child.StandardOutput, Console.Out);
	var errorPump = PumpAsync(child.StandardError, Console.Error);
	await child.WaitForExitAsync();
	await Task.WhenAll(outputPump, errorPump);
	return child.ExitCode;
}

Console.WriteLine("collector-output");
Console.WriteLine("ready");

switch (args[0])
{
	case "exit-on-stdin":
		await Console.In.ReadLineAsync();
		return 0;

	case "finalize-on-stdin" when args.Length == 2:
		await Console.In.ReadLineAsync();
		Console.WriteLine("finalizing");
		while (!File.Exists(args[1]))
			await Task.Delay(10);
		return 0;

	case "ignore-stdin":
		await Task.Delay(Timeout.InfiniteTimeSpan);
		return 0;

	default:
		return 2;
}

static async Task PumpAsync(StreamReader reader, TextWriter writer)
{
	while (await reader.ReadLineAsync() is { } line)
		await writer.WriteLineAsync(line);
}
