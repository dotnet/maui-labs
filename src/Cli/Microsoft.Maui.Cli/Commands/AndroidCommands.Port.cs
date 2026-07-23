// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Android;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AndroidCommands
{
	// Shared across all `port` subcommands. Recursive so subcommands inherit it.
	static readonly Option<string> s_portDeviceOption = new("--device", "-s")
	{
		Description = "Target device serial. Defaults to the only online device, or the ANDROID_SERIAL environment variable.",
		Recursive = true
	};

	static Command CreatePortCommand()
	{
		var command = new Command("port", "Manage adb forward/reverse port rules for a device");
		command.Add(s_portDeviceOption);

		command.Add(CreatePortListCommand());
		command.Add(CreatePortForwardCommand());
		command.Add(CreatePortReverseCommand());
		command.Add(CreatePortClearCommand());

		return command;
	}

	static Command CreatePortListCommand()
	{
		var forwardFlag = new Option<bool>("--forward") { Description = "Show only forward (host → device) rules" };
		var reverseFlag = new Option<bool>("--reverse") { Description = "Show only reverse (device → host) rules" };

		var listCommand = new Command("list", "List active adb forward/reverse port rules")
		{
			forwardFlag,
			reverseFlag
		};

		listCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
		{
			var provider = GetAndroidProvider(parseResult);
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			try
			{
				var serial = await ResolveDeviceSerialAsync(provider, parseResult.GetValue(s_portDeviceOption), cancellationToken);

				var showForward = parseResult.GetValue(forwardFlag);
				var showReverse = parseResult.GetValue(reverseFlag);
				if (!showForward && !showReverse)
					showForward = showReverse = true;

				IReadOnlyList<AndroidPortMapping> forward = showForward
					? await provider.ListForwardPortsAsync(serial, cancellationToken)
					: Array.Empty<AndroidPortMapping>();
				IReadOnlyList<AndroidPortMapping> reverse = showReverse
					? await provider.ListReversePortsAsync(serial, cancellationToken)
					: Array.Empty<AndroidPortMapping>();

				if (useJson)
				{
					formatter.Write(new AndroidPortListResult
					{
						Serial = serial,
						Forward = forward.ToList(),
						Reverse = reverse.ToList()
					});
				}
				else
				{
					if (showForward)
					{
						formatter.WriteInfo($"Forward (host → device) — {serial}");
						if (forward.Count == 0)
							formatter.WriteInfo("  (none)");
						else
							formatter.WriteTable(
								forward,
								("Host", m => m.Local.ToString()),
								("Device", m => m.Remote.ToString()),
								("Protocol", m => m.Protocol));
					}

					if (showReverse)
					{
						formatter.WriteInfo($"Reverse (device → host) — {serial}");
						if (reverse.Count == 0)
							formatter.WriteInfo("  (none)");
						else
							formatter.WriteTable(
								reverse,
								("Device", m => m.Local.ToString()),
								("Host", m => m.Remote.ToString()),
								("Protocol", m => m.Protocol));
					}
				}

				return 0;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return listCommand;
	}

	static Command CreatePortForwardCommand()
	{
		var portArg = new Argument<int?>("port")
		{
			Description = "Host port to forward from (tcp)",
			Arity = ArgumentArity.ZeroOrOne
		};
		var remoteArg = new Argument<int?>("remote")
		{
			Description = "Device port to forward to (tcp). Defaults to <port>.",
			Arity = ArgumentArity.ZeroOrOne
		};
		var agentPortOption = new Option<int?>("--agent-port")
		{
			Description = "DevFlow shortcut: forward the agent port on both host and device sides"
		};

		var forwardCommand = new Command("forward", "Forward a host port to a device port (adb forward)")
		{
			portArg,
			remoteArg,
			agentPortOption
		};

		forwardCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
		{
			var provider = GetAndroidProvider(parseResult);
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			try
			{
				var port = parseResult.GetValue(portArg);
				var remote = parseResult.GetValue(remoteArg);
				var agentPort = parseResult.GetValue(agentPortOption);

				if (port is not null && agentPort is not null)
					throw new MauiToolException(ErrorCodes.InvalidArgument, "Specify either a <port> argument or --agent-port, not both.");

				var hostPort = port ?? agentPort;
				if (hostPort is null)
					throw new MauiToolException(ErrorCodes.InvalidArgument,
						"Specify a host port to forward, e.g. 'maui android port forward 8080' or '--agent-port 19223'.");

				ValidatePort(hostPort.Value);
				var devicePort = remote ?? hostPort.Value;
				ValidatePort(devicePort);

				var serial = await ResolveDeviceSerialAsync(provider, parseResult.GetValue(s_portDeviceOption), cancellationToken);
				await provider.AddForwardPortAsync(serial, hostPort.Value, devicePort, cancellationToken);

				var message = $"Forwarding host tcp:{hostPort} → device tcp:{devicePort} on {serial}.";
				if (useJson)
				{
					formatter.Write(new AndroidPortActionResult
					{
						Action = "forward",
						Serial = serial,
						Local = hostPort,
						Remote = devicePort,
						Protocol = "tcp",
						Message = message
					});
				}
				else
				{
					formatter.WriteSuccess(message);
				}

				return 0;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return forwardCommand;
	}

	static Command CreatePortReverseCommand()
	{
		var portArg = new Argument<int?>("port")
		{
			Description = $"Device port to reverse (tcp). Defaults to the broker port {BrokerServer.DefaultPort}.",
			Arity = ArgumentArity.ZeroOrOne
		};
		var hostArg = new Argument<int?>("host")
		{
			Description = "Host port to reverse to (tcp). Defaults to <port>.",
			Arity = ArgumentArity.ZeroOrOne
		};

		var reverseCommand = new Command("reverse", "Reverse a device port to a host port (adb reverse)")
		{
			portArg,
			hostArg
		};

		reverseCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
		{
			var provider = GetAndroidProvider(parseResult);
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			try
			{
				var devicePort = parseResult.GetValue(portArg) ?? BrokerServer.DefaultPort;
				ValidatePort(devicePort);
				var hostPort = parseResult.GetValue(hostArg) ?? devicePort;
				ValidatePort(hostPort);

				var serial = await ResolveDeviceSerialAsync(provider, parseResult.GetValue(s_portDeviceOption), cancellationToken);
				await provider.AddReversePortAsync(serial, devicePort, hostPort, cancellationToken);

				var message = $"Reversing device tcp:{devicePort} → host tcp:{hostPort} on {serial}.";
				if (useJson)
				{
					formatter.Write(new AndroidPortActionResult
					{
						Action = "reverse",
						Serial = serial,
						Local = devicePort,
						Remote = hostPort,
						Protocol = "tcp",
						Message = message
					});
				}
				else
				{
					formatter.WriteSuccess(message);
				}

				return 0;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return reverseCommand;
	}

	static Command CreatePortClearCommand()
	{
		var forwardFlag = new Option<bool>("--forward") { Description = "Clear only forward (host → device) rules" };
		var reverseFlag = new Option<bool>("--reverse") { Description = "Clear only reverse (device → host) rules" };

		var clearCommand = new Command("clear", "Remove all adb forward/reverse rules for a device")
		{
			forwardFlag,
			reverseFlag
		};

		clearCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
		{
			var provider = GetAndroidProvider(parseResult);
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			try
			{
				var clearForward = parseResult.GetValue(forwardFlag);
				var clearReverse = parseResult.GetValue(reverseFlag);
				if (!clearForward && !clearReverse)
					clearForward = clearReverse = true;

				var serial = await ResolveDeviceSerialAsync(provider, parseResult.GetValue(s_portDeviceOption), cancellationToken);

				if (clearForward)
					await provider.ClearForwardPortsAsync(serial, cancellationToken);
				if (clearReverse)
					await provider.ClearReversePortsAsync(serial, cancellationToken);

				var cleared = (clearForward, clearReverse) switch
				{
					(true, true) => "forward and reverse",
					(true, false) => "forward",
					_ => "reverse"
				};
				var message = $"Cleared {cleared} port rules on {serial}.";

				if (useJson)
				{
					formatter.Write(new AndroidPortActionResult
					{
						Action = "clear",
						Serial = serial,
						ClearedForward = clearForward,
						ClearedReverse = clearReverse,
						Message = message
					});
				}
				else
				{
					formatter.WriteSuccess(message);
				}

				return 0;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return clearCommand;
	}

	/// <summary>
	/// Resolves the target device serial: an explicit <c>--device</c> value (falling back to the
	/// <c>ANDROID_SERIAL</c> environment variable) must match an online device; otherwise the single
	/// online Android device is used. Throws when none, multiple (without a selector), or the requested
	/// device is not online.
	/// </summary>
	static async Task<string> ResolveDeviceSerialAsync(IAndroidProvider provider, string? requested, CancellationToken cancellationToken)
	{
		var target = !string.IsNullOrWhiteSpace(requested)
			? requested
			: Environment.GetEnvironmentVariable("ANDROID_SERIAL");

		var devices = await provider.GetDevicesAsync(cancellationToken);
		var online = devices
			.Where(d => d.Platforms.Any(p => p.Equals("android", StringComparison.OrdinalIgnoreCase)))
			.Where(d => d.State is DeviceState.Connected or DeviceState.Booted)
			.ToList();

		if (!string.IsNullOrWhiteSpace(target))
		{
			var match = online.FirstOrDefault(d => d.Id.Equals(target, StringComparison.OrdinalIgnoreCase));
			if (match is null)
				throw new MauiToolException(ErrorCodes.AndroidDeviceNotFound,
					$"Android device '{target}' is not connected and online.");
			return match.Id;
		}

		return online.Count switch
		{
			0 => throw new MauiToolException(ErrorCodes.AndroidDeviceNotFound,
				"No online Android devices or emulators were found."),
			1 => online[0].Id,
			_ => throw new MauiToolException(ErrorCodes.InvalidArgument,
				"Multiple online Android devices or emulators were found. Specify --device or ANDROID_SERIAL.")
		};
	}

	static void ValidatePort(int port)
	{
		if (port is < 1 or > 65535)
			throw new MauiToolException(ErrorCodes.InvalidArgument, $"Port must be between 1 and 65535, got {port}.");
	}
}
