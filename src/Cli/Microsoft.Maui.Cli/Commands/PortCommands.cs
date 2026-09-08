// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Port;

namespace Microsoft.Maui.Cli.Commands;

internal static class PortCommands
{
	public static Command Create()
	{
		var portCommand = new Command("port", "Diagnose TCP port usage.");
		portCommand.Add(CreateCheckCommand());
		return portCommand;
	}

	private static Command CreateCheckCommand()
	{
		var portArg = new Argument<int>("port") { Description = "TCP port number to check (1-65535)." };

		var checkCommand = new Command("check", "Check which process holds a TCP port.")
		{
			portArg,
		};

		checkCommand.SetAction((ParseResult parseResult) =>
		{
			var port = parseResult.GetValue(portArg);
			var formatter = Program.GetFormatter(parseResult);

			if (port is < 1 or > 65535)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Port must be between 1 and 65535, got {port}."));
				return 2;
			}

			IPortInspector inspector;
			try
			{
				if (OperatingSystem.IsWindows())
				{
					inspector = new WindowsPortInspector();
				}
				else
				{
#pragma warning disable CA1416
					inspector = new UnixPortInspector();
#pragma warning restore CA1416
				}
			}
			catch (Exception ex)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PortEnumerationFailed, ex.Message));
				return 2;
			}

			List<PortListenerInfo> raw;
			try
			{
				raw = [.. inspector.GetListeners(port)];
			}
			catch (Exception ex)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PortEnumerationFailed, ex.Message));
				return 2;
			}

			var listeners = raw.Select(l => new PortListenerResult
			{
				Pid = l.Pid,
				ProcessName = l.ProcessName,
				Address = l.Address,
				Family = l.Family,
				State = l.State,
			}).ToList();

			var result = new PortCheckResult { Port = port, InUse = listeners.Count > 0, Listeners = listeners };

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
			{
				formatter.Write(result);
			}
			else
			{
				if (!result.InUse)
				{
					formatter.WriteSuccess($"Port {port} is free.");
				}
				else
				{
					formatter.WriteInfo($"Port {port} is in use:");
					foreach (var l in result.Listeners)
					{
						var processInfo = l.Pid > 0 ? $"PID {l.Pid} ({(string.IsNullOrEmpty(l.ProcessName) ? "unknown" : l.ProcessName)})" : "unknown process";
						formatter.WriteInfo($"  {processInfo} {l.Address} [{l.Family}]");
					}
				}
			}

			return result.InUse ? 1 : 0;
		});

		return checkCommand;
	}
}
