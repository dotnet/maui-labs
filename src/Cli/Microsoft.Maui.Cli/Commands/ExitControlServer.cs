// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal sealed class ExitControlServer : IDisposable
{
	readonly TcpListener _listener;
	readonly Task<TcpClient?> _acceptTask;
	readonly TaskCompletionSource<bool> _clientClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
	readonly IOutputFormatter _formatter;
	readonly bool _useJson;
	readonly bool _verbose;
	bool _disposed;
	TcpClient? _client;

	ExitControlServer(TcpListener listener, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		_listener = listener;
		_formatter = formatter;
		_useJson = useJson;
		_verbose = verbose;
		_acceptTask = AcceptClientAsync();
	}

	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	public static ExitControlServer Attach(ReservedTcpPort reservation, IOutputFormatter formatter, bool useJson, bool verbose) =>
		new(reservation.DetachListener(), formatter, useJson, verbose);

	public async Task<bool> TryRequestExitAsync(TimeSpan connectTimeout, TimeSpan commandTimeout, CancellationToken cancellationToken)
	{
		var client = await WaitForClientAsync(connectTimeout, cancellationToken);
		if (client is null)
			return false;

		try
		{
			LogVerbose($"Sending graceful exit command over the startup profiling control channel on port {Port}.");
			using var writer = new StreamWriter(client.GetStream(), Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
			await writer.WriteLineAsync("exit");
			await writer.FlushAsync();

			await _clientClosedTcs.Task.WaitAsync(commandTimeout, cancellationToken);
			LogVerbose("The profiled app acknowledged the exit command and closed the control channel.");
			return true;
		}
		catch (TimeoutException)
		{
			LogVerbose("Timed out waiting for the profiled app to close after the exit command.");
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	async Task<TcpClient?> WaitForClientAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		try
		{
			var completed = await Task.WhenAny(_acceptTask, Task.Delay(timeout, cancellationToken));
			if (completed != _acceptTask)
				return null;

			_client ??= await _acceptTask;
			return _client;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	async Task<TcpClient?> AcceptClientAsync()
	{
		try
		{
			var client = await _listener.AcceptTcpClientAsync();
			LogVerbose($"Startup profiling exit control client connected from {client.Client.RemoteEndPoint}.");
			_ = MonitorClientAsync(client);
			return client;
		}
		catch (ObjectDisposedException)
		{
			return null;
		}
		catch (SocketException ex)
		{
			LogVerbose($"Exit control server stopped accepting clients: {ex.Message}");
			return null;
		}
	}

	async Task MonitorClientAsync(TcpClient client)
	{
		try
		{
			using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
			while (true)
			{
				var message = await reader.ReadLineAsync();
				if (message is null)
					break;

				LogVerbose($"Exit control channel message received: '{message.Trim()}'.");
			}
		}
		catch (IOException ex)
		{
			LogVerbose($"Exit control channel read loop ended: {ex.Message}");
		}
		catch (ObjectDisposedException)
		{
		}
		finally
		{
			_clientClosedTcs.TrySetResult(true);
		}
	}

	void LogVerbose(string message)
	{
		if (_verbose && !_useJson)
			_formatter.WriteProgress($"[debug] {message}");
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		try
		{
			_client?.Dispose();
			_listener.Stop();
		}
		catch
		{
			// Best-effort cleanup only.
		}

		_disposed = true;
	}
}
