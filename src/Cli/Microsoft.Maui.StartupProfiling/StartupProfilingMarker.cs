// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.Maui.StartupProfiling;

/// <summary>
/// Public API for signalling the end of MAUI startup to an attached dotnet-trace collector.
/// </summary>
/// <remarks>
/// <para>
/// Add the <c>Microsoft.Maui.StartupProfiling</c> NuGet package to your MAUI app project,
/// then call <see cref="Complete"/> at the logical end of startup — for example after the
/// first page is shown or after <c>Application.Current.MainPage</c> is fully constructed.
/// </para>
/// <para>
/// When running under <c>maui profile startup</c> or any dotnet-trace session configured with
/// <c>--stopping-event-provider-name Microsoft.Maui.StartupProfiling
/// --stopping-event-event-name StartupComplete</c>), the trace will stop automatically
/// when this method is called.
/// </para>
/// </remarks>
public static class StartupProfilingMarker
{
	internal const string ProfilingEnvironmentVariable = "MAUI_STARTUP_PROFILING";
	internal const string ExitControlHostEnvironmentVariable = "MAUI_STARTUP_PROFILING_EXIT_HOST";
	internal const string ExitControlPortEnvironmentVariable = "MAUI_STARTUP_PROFILING_EXIT_PORT";

	/// <summary>
	/// The EventSource provider name to use with
	/// <c>dotnet-trace --stopping-event-provider-name</c>.
	/// </summary>
	public const string ProviderName = StartupProfilingEventSource.ProviderName;

	/// <summary>
	/// The event name to use with <c>dotnet-trace --stopping-event-event-name</c>.
	/// </summary>
	public const string EventName = StartupProfilingEventSource.StartupCompleteEventName;

	/// <summary>
	/// Returns <see langword="true"/> when the app is running under startup profiling.
	/// </summary>
	public static bool IsProfilingSession =>
		IsEnabledEnvironmentVariable(ProfilingEnvironmentVariable)
		|| StartupProfilingExitChannel.IsConfigured;

	/// <summary>
	/// Signals that startup is complete.
	/// </summary>
	public static void Complete() => StartupProfilingEventSource.Log.StartupComplete();

	internal static bool IsEnabledEnvironmentVariable(string variableName)
	{
		var value = Environment.GetEnvironmentVariable(variableName);
		return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// Module initializer that eagerly instantiates the <see cref="StartupProfilingEventSource"/>
/// so the provider is visible to dotnet-trace from the very first moment the assembly loads —
/// before any app code runs. This is important for startup profiling because the collector
/// must see the provider before it can register the stopping-event filter.
/// </summary>
internal static class StartupProfilingInitializer
{
	[ModuleInitializer]
	internal static void Initialize()
	{
		_ = StartupProfilingEventSource.Log;
		StartupProfilingExitChannel.TryStart();
	}
}

internal static class StartupProfilingExitChannel
{
	const int MaxConnectAttempts = 20;
	static readonly TimeSpan s_retryDelay = TimeSpan.FromMilliseconds(500);
	static int s_started;

	internal static bool IsConfigured => TryGetEndpoint(out _, out _);

	internal static void TryStart()
	{
		if (Interlocked.Exchange(ref s_started, 1) != 0)
			return;

		if (!TryGetEndpoint(out var host, out var port))
			return;

		_ = Task.Run(() => RunAsync(host, port));
	}

	internal static bool TryGetEndpoint(out string host, out int port)
	{
		host = "127.0.0.1";
		port = 0;

		var explicitPort = Environment.GetEnvironmentVariable(StartupProfilingMarker.ExitControlPortEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(explicitPort)
			|| !int.TryParse(explicitPort, out var parsedPort)
			|| parsedPort <= 0
			|| parsedPort > IPEndPoint.MaxPort)
		{
			return false;
		}

		var explicitHost = Environment.GetEnvironmentVariable(StartupProfilingMarker.ExitControlHostEnvironmentVariable);
		host = string.IsNullOrWhiteSpace(explicitHost) ? "127.0.0.1" : explicitHost.Trim();
		port = parsedPort;
		return true;
	}

	static async Task RunAsync(string host, int port)
	{
		for (var attempt = 0; attempt < MaxConnectAttempts; attempt++)
		{
			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(host, port).ConfigureAwait(false);

				using var stream = client.GetStream();
				using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
				using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

				await writer.WriteLineAsync($"ready pid={Environment.ProcessId}").ConfigureAwait(false);

				while (await reader.ReadLineAsync().ConfigureAwait(false) is { } message)
				{
					if (string.Equals(message.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
					{
						Environment.Exit(0);
					}
				}

				return;
			}
			catch (SocketException)
			{
				if (attempt + 1 == MaxConnectAttempts)
					return;
			}
			catch (IOException)
			{
				if (attempt + 1 == MaxConnectAttempts)
					return;
			}

			await Task.Delay(s_retryDelay).ConfigureAwait(false);
		}
	}
}
