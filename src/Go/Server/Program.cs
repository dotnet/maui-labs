// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Go.Server;

// Entry point
if (args.Length == 0)
{
	Console.Error.WriteLine("Usage: Microsoft.Maui.Go.Server <project-dir> [--port 9000] [--no-qr]");
	return 1;
}

var projectDir = args[0];
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

return await GoDevServer.RunAsync(projectDir, port, showQr, cts.Token);
