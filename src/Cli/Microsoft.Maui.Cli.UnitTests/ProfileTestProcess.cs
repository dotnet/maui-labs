// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length == 0)
	return 2;

if ((args[0] == "wrap-ignore-stdin" && args.Length == 2)
	|| (args[0] == "wrap-write-until-killed" && args.Length == 4))
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
	foreach (var argument in new[]
	{
		"run",
		"--file",
		args[1],
		"--no-launch-profile",
		"--",
		args[0] == "wrap-ignore-stdin" ? "ignore-stdin" : "write-until-killed"
	})
		startInfo.ArgumentList.Add(argument);
	if (args[0] == "wrap-write-until-killed")
	{
		startInfo.ArgumentList.Add(args[2]);
		startInfo.ArgumentList.Add(args[3]);
	}

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

	case "write-until-killed" when args.Length == 3:
		await File.WriteAllTextAsync(args[2], "ready");
		var writeCount = 0;
		while (true)
		{
			Directory.CreateDirectory(args[1]);
			await File.WriteAllTextAsync(Path.Combine(args[1], "child-output.txt"), (++writeCount).ToString());
			await Task.Delay(20);
		}

	default:
		return 2;
}

static async Task PumpAsync(StreamReader reader, TextWriter writer)
{
	while (await reader.ReadLineAsync() is { } line)
		await writer.WriteLineAsync(line);
}
