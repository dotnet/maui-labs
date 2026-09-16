// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

if (args.Length == 0)
	return 2;

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
