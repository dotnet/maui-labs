// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Go.Server;

// Entry point
if (args.Length == 0)
{
	Console.Error.WriteLine("Usage: Microsoft.Maui.Go.Server <project-dir-or-file.cs> [--port 9000] [--no-qr]");
	Console.Error.WriteLine();
	Console.Error.WriteLine("  <project-dir>   Directory containing a .csproj (project mode)");
	Console.Error.WriteLine("  <file.cs>        Single .cs file (single-file mode)");
	return 1;
}

var target = args[0];
var port = 9000;
var showQr = true;

for (var i = 1; i < args.Length; i++)
{
	if (args[i] == "--port" && i + 1 < args.Length)
		port = int.Parse(args[++i]);
	else if (args[i] == "--no-qr")
		showQr = false;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Detect single-file mode: argument ends with .cs and is a file
if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
	return await GoDevServer.RunSingleFileAsync(target, port, showQr, cts.Token);

// Otherwise treat as project directory
return await GoDevServer.RunAsync(target, port, showQr, cts.Token);
