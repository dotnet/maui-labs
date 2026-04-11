// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal sealed class MonitoredProcess : IDisposable
{
	readonly Task _stdoutPump;
	readonly Task _stderrPump;

	MonitoredProcess(
		Process process,
		StringBuilder standardOutput,
		StringBuilder standardError,
		Task stdoutPump,
		Task stderrPump)
	{
		Process = process;
		StandardOutput = standardOutput;
		StandardError = standardError;
		_stdoutPump = stdoutPump;
		_stderrPump = stderrPump;
	}

	public Process Process { get; }
	public StringBuilder StandardOutput { get; }
	public StringBuilder StandardError { get; }

	public static MonitoredProcess Attach(
		Process process,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		string prefix,
		CancellationToken cancellationToken,
		Action<string>? onStdoutLine = null,
		Action<string>? onStderrLine = null)
	{
		var stdout = new StringBuilder();
		var stderr = new StringBuilder();

		var stdoutPump = PumpStreamAsync(
			process.StandardOutput,
			stdout,
			line =>
			{
				onStdoutLine?.Invoke(line);
				if (verbose && !useJson)
					formatter.WriteProgress($"[{prefix}] {line}");
			},
			cancellationToken);

		var stderrPump = PumpStreamAsync(
			process.StandardError,
			stderr,
			line =>
			{
				onStderrLine?.Invoke(line);
				if (verbose && !useJson)
					formatter.WriteProgress($"[{prefix}:stderr] {line}");
			},
			cancellationToken);

		return new MonitoredProcess(process, stdout, stderr, stdoutPump, stderrPump);
	}

	public async Task WaitForExitAsync()
	{
		await Process.WaitForExitAsync();
		await Task.WhenAll(_stdoutPump, _stderrPump);
	}

	public string GetCombinedOutput()
	{
		var builder = new StringBuilder();
		if (StandardOutput.Length > 0)
			builder.AppendLine(StandardOutput.ToString().Trim());
		if (StandardError.Length > 0)
			builder.AppendLine(StandardError.ToString().Trim());
		return builder.ToString().Trim();
	}

	public void Dispose() => Process.Dispose();

	static async Task PumpStreamAsync(
		StreamReader reader,
		StringBuilder buffer,
		Action<string>? onLine,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line == null)
				break;

			buffer.AppendLine(line);
			onLine?.Invoke(line);
		}
	}
}
